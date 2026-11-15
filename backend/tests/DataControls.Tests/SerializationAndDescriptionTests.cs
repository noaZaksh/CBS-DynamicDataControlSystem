using System.Text.Json;
using DataControls.Application.Ai;
using DataControls.Application.Describing;
using DataControls.Application.Execution;
using DataControls.Application.Validation;
using DataControls.Domain.Definitions;
using DataControls.Infrastructure.Sql;
using Microsoft.Extensions.Logging.Abstractions;
using static DataControls.Tests.TestModel;

namespace DataControls.Tests;

public class SerializationAndDescriptionTests
{
    private readonly DefinitionDescriber _describer = new();

    [Fact]
    public void Definition_roundtrips_through_json_regardless_of_property_order()
    {
        // "type" אינו השדה הראשון - כפי שעלול להגיע ממודל AI
        const string json = """
            {"rootEntity":"employee","filter":{"logic":"and","type":"group","id":"g1","children":[
              {"field":"age","operator":"lt","type":"condition","id":"c1","value":{"kind":"constant","value":18}},
              {"relation":"assignments","type":"related","quantifier":"none"}]}}
            """;

        var def = DefinitionSerializer.Deserialize(json);
        var again = DefinitionSerializer.Deserialize(DefinitionSerializer.Serialize(def));

        Assert.Equal("employee", again.RootEntity);
        var c1 = Assert.IsType<ConditionNode>(again.Filter.Children[0]);
        Assert.Equal(ComparisonOperator.Lt, c1.Operator);
        var r = Assert.IsType<RelatedNode>(again.Filter.Children[1]);
        Assert.Equal(Quantifier.None, r.Quantifier);
        Assert.Contains("\"type\":\"condition\"", DefinitionSerializer.Serialize(def));
    }

    [Fact]
    public void Unknown_node_type_is_rejected()
    {
        Assert.Throws<JsonException>(() => DefinitionSerializer.Deserialize(
            """{"rootEntity":"employee","filter":{"type":"group","children":[{"type":"sql","text":"DROP TABLE x"}]}}"""));
    }

    [Fact]
    public void Describes_definition_in_hebrew()
    {
        var def = Def("employee", LogicalOperator.And,
            Cond("c1", "age", ComparisonOperator.Lt, ValueSpec.Constant(18)),
            Cond("c2", "monthlySalary", ComparisonOperator.Gt, ValueSpec.Constant(15000)),
            Cond("c3", "department.isActive", ComparisonOperator.Eq, ValueSpec.Constant(false)));

        var text = _describer.Describe(def, Create());

        Assert.Equal("עובדים שבהם: גיל קטן מ-18 וגם שכר חודשי גדול מ-15,000 וגם מחלקה פעילה (המחלקה של העובד) = לא", text);
    }

    [Fact]
    public void Describes_dates_and_related()
    {
        var model = Create();
        Assert.Equal("עובדים שבהם: תאריך סיום העסקה מוקדם מתאריך תחילת העסקה",
            _describer.Describe(Def("employee", LogicalOperator.And,
                Cond("c1", "endDate", ComparisonOperator.Lt, ValueSpec.OfField("startDate"))), model));

        var related = Def("department", LogicalOperator.And, new RelatedNode
        {
            Id = "r1", Relation = "employees", Quantifier = Quantifier.None,
            Filter = new GroupNode { Id = "g2", Children = { Cond("c2", "isActive", ComparisonOperator.Eq, ValueSpec.Constant(true)) } }
        });
        Assert.Equal("מחלקות שבהם: אין אף רשומה ב\"העובדים במחלקה\" שבה עובד פעיל = כן",
            _describer.Describe(related, model));
    }

    [Fact]
    public void Reason_builder_explains_only_conditions_that_matched()
    {
        var model = Create();
        var def = Def("employee", LogicalOperator.Or,
            Cond("c1", "email", ComparisonOperator.IsEmpty),
            Cond("c2", "monthlySalary", ComparisonOperator.Lte, ValueSpec.Constant(0)));
        var query = new SqlServerQueryCompiler().Compile(def, model, new QueryOptions(10));

        var row = new Dictionary<string, object?>
        {
            ["__key"] = 21,
            [query.AliasForField("email")!] = "maya@example.com",
            [query.AliasForField("monthlySalary")!] = -500m,
            ["__f_g1"] = 1,
            ["__f_c1"] = 0,
            ["__f_c2"] = 1
        };

        var (reasons, matched) = new ReasonBuilder(_describer).Explain(def, model, query, row);

        Assert.Equal(new[] { "g1", "c2" }, matched);
        Assert.Equal("שכר חודשי: -500 (קטן או שווה ל-0)", Assert.Single(reasons));
    }

    [Fact]
    public void Ai_response_is_validated_like_user_input()
    {
        var service = new AiControlService(null!, null!, new ControlDefinitionValidator(), _describer,
            NullLogger<AiControlService>.Instance);
        var model = Create();

        var good = service.ToDraft(new AiInterpretation
        {
            Name = "קטינים עם שכר גבוה",
            Severity = "high",
            Confidence = 0.9,
            Definition = JsonDocument.Parse("""
                {"rootEntity":"employee","filter":{"type":"group","logic":"and","children":[
                  {"type":"condition","field":"age","operator":"lt","value":{"kind":"constant","value":18}}]}}
                """).RootElement
        }, model);

        var hallucinated = service.ToDraft(new AiInterpretation
        {
            Definition = JsonDocument.Parse("""
                {"rootEntity":"employee","filter":{"type":"group","children":[
                  {"type":"condition","field":"bonus","operator":"gt","value":{"kind":"constant","value":1}}]}}
                """).RootElement
        }, model);

        Assert.True(good.IsValid);
        Assert.Equal("עובדים שבהם: גיל קטן מ-18", good.Summary);
        Assert.Equal(Domain.Controls.Severity.High, good.Severity);
        Assert.False(hallucinated.IsValid);
        Assert.NotEmpty(hallucinated.ValidationErrors);
    }
}
