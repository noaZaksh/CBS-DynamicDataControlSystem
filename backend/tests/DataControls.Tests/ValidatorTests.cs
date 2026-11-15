using DataControls.Application.Validation;
using DataControls.Domain.Definitions;
using static DataControls.Tests.TestModel;

namespace DataControls.Tests;

public class ValidatorTests
{
    private readonly ControlDefinitionValidator _validator = new();

    private ValidationResult Validate(ControlDefinition def) =>
        _validator.Validate(DefinitionNormalizer.Normalize(def), Create());

    [Fact]
    public void Valid_definition_passes()
    {
        var result = Validate(Def("employee", LogicalOperator.And,
            Cond("c1", "age", ComparisonOperator.Lt, ValueSpec.Constant(18)),
            Cond("c2", "department.isActive", ComparisonOperator.Eq, ValueSpec.Constant(false))));

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void Unknown_root_entity_fails()
    {
        var result = Validate(Def("payroll", LogicalOperator.And, Cond("c1", "age", ComparisonOperator.IsEmpty)));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("salary")]                       // שדה לא קיים
    [InlineData("age; DROP TABLE x")]            // ניסיון הזרקה בשם שדה
    [InlineData("department.budget")]            // שדה לא קיים בישות הקשורה
    [InlineData("assignments.allocationPercent")]// קשר אחד-לרבים בנתיב שדה - אסור
    public void Invalid_field_paths_fail(string field)
    {
        var result = Validate(Def("employee", LogicalOperator.And, Cond("c1", field, ComparisonOperator.IsEmpty)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Operator_must_match_data_type()
    {
        var result = Validate(Def("employee", LogicalOperator.And,
            Cond("c1", "age", ComparisonOperator.Contains, ValueSpec.Constant("1"))));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Value_must_be_convertible()
    {
        var result = Validate(Def("employee", LogicalOperator.And,
            Cond("c1", "startDate", ComparisonOperator.Gt, ValueSpec.Constant("not a date"))));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Missing_value_fails_but_isEmpty_needs_none()
    {
        Assert.False(Validate(Def("employee", LogicalOperator.And, Cond("c1", "age", ComparisonOperator.Gt))).IsValid);
        Assert.True(Validate(Def("employee", LogicalOperator.And, Cond("c1", "age", ComparisonOperator.IsEmpty))).IsValid);
    }

    [Fact]
    public void Field_comparison_requires_same_type()
    {
        var result = Validate(Def("employee", LogicalOperator.And,
            Cond("c1", "age", ComparisonOperator.Lt, ValueSpec.OfField("startDate"))));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Today_only_for_dates()
    {
        Assert.False(Validate(Def("employee", LogicalOperator.And,
            Cond("c1", "age", ComparisonOperator.Lt, ValueSpec.TodayPlus()))).IsValid);
        Assert.True(Validate(Def("employee", LogicalOperator.And,
            Cond("c1", "endDate", ComparisonOperator.Lt, ValueSpec.TodayPlus()))).IsValid);
    }

    [Fact]
    public void Empty_group_fails()
    {
        Assert.False(Validate(Def("employee", LogicalOperator.And)).IsValid);
    }

    [Fact]
    public void Too_deep_nesting_fails()
    {
        var group = new GroupNode { Children = { Cond("", "age", ComparisonOperator.IsEmpty) } };
        for (var i = 0; i < ControlDefinitionValidator.MaxDepth + 1; i++)
            group = new GroupNode { Children = { group } };

        Assert.False(Validate(new ControlDefinition { RootEntity = "employee", Filter = group }).IsValid);
    }

    [Fact]
    public void Related_node_validates_inner_fields_against_target_entity()
    {
        var ok = Def("department", LogicalOperator.And, new RelatedNode
        {
            Relation = "employees", Quantifier = Quantifier.None,
            Filter = new GroupNode { Children = { Cond("", "isActive", ComparisonOperator.Eq, ValueSpec.Constant(true)) } }
        });
        var bad = Def("department", LogicalOperator.And, new RelatedNode
        {
            Relation = "employees",
            Filter = new GroupNode { Children = { Cond("", "departmentName", ComparisonOperator.IsEmpty) } } // שדה של מחלקה, לא של עובד
        });

        Assert.True(Validate(ok).IsValid);
        Assert.False(Validate(bad).IsValid);
    }

    [Fact]
    public void Normalizer_assigns_unique_ids()
    {
        var def = Def("employee", LogicalOperator.And,
            Cond("dup", "age", ComparisonOperator.IsEmpty),
            Cond("dup", "email", ComparisonOperator.IsEmpty),
            Cond("bad id!", "email", ComparisonOperator.IsEmpty));
        DefinitionNormalizer.Normalize(def);

        var ids = def.Filter.Children.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches("^[A-Za-z0-9_]+$", id!));
    }
}
