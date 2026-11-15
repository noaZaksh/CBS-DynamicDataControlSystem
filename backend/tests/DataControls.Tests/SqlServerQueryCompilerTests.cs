using DataControls.Application.Execution;
using DataControls.Domain.Definitions;
using DataControls.Infrastructure.Sql;
using static DataControls.Tests.TestModel;

namespace DataControls.Tests;

public class SqlServerQueryCompilerTests
{
    private readonly SqlServerQueryCompiler _compiler = new();
    private readonly QueryOptions _options = new(MaxRows: 100);

    private CompiledQuery Compile(ControlDefinition def) => _compiler.Compile(def, Create(), _options);

    [Fact]
    public void Simple_and_condition_uses_parameters_not_literals()
    {
        var q = Compile(Def("employee", LogicalOperator.And,
            Cond("c1", "age", ComparisonOperator.Lt, ValueSpec.Constant(18)),
            Cond("c2", "monthlySalary", ComparisonOperator.Gt, ValueSpec.Constant(15000))));

        Assert.Contains("FROM [hr].[vw_EmployeesEnriched] AS t0", q.Sql);
        Assert.Contains("(t0.[Age] < @p0 AND t0.[MonthlySalary] > @p1)", q.Sql);
        Assert.DoesNotContain("15000", q.Sql);
        Assert.Equal(18m, q.Parameters[0].Value);
        Assert.Equal(15000m, q.Parameters[1].Value);
        Assert.Contains("SELECT TOP (100)", q.Sql);
    }

    [Fact]
    public void Value_injection_attempt_is_passed_as_parameter()
    {
        const string evil = "x'; DROP TABLE hr.Employees; --";
        var q = Compile(Def("employee", LogicalOperator.And,
            Cond("c1", "email", ComparisonOperator.Eq, ValueSpec.Constant(evil))));

        Assert.DoesNotContain("DROP", q.Sql);
        Assert.Equal(evil, q.Parameters.Single().Value);
    }

    [Fact]
    public void Field_path_through_relation_adds_single_left_join()
    {
        var q = Compile(Def("employee", LogicalOperator.Or,
            Cond("c1", "department.isActive", ComparisonOperator.Eq, ValueSpec.Constant(false)),
            Cond("c2", "department.departmentName", ComparisonOperator.IsEmpty)));

        var joinLine = "LEFT JOIN [hr].[Departments] AS t1 ON t1.[DepartmentId] = t0.[DepartmentId]";
        Assert.Contains(joinLine, q.Sql);
        // אותו JOIN מופיע פעם אחת בשאילתת הספירה ופעם אחת בשאילתת הרשומות - לא יותר
        Assert.Equal(2, CountOccurrences(q.Sql, "LEFT JOIN"));
        Assert.Contains("(t1.[DepartmentName] IS NULL OR LTRIM(RTRIM(t1.[DepartmentName])) = N'')", q.Sql);
    }

    [Fact]
    public void Nested_relation_path_chains_joins()
    {
        var q = Compile(Def("employee", LogicalOperator.And,
            Cond("c1", "department.manager.isActive", ComparisonOperator.Eq, ValueSpec.Constant(false))));

        Assert.Contains("LEFT JOIN [hr].[Departments] AS t1 ON t1.[DepartmentId] = t0.[DepartmentId]", q.Sql);
        Assert.Contains("LEFT JOIN [hr].[vw_EmployeesEnriched] AS t2 ON t2.[EmployeeId] = t1.[ManagerEmployeeId]", q.Sql);
        Assert.Contains("t2.[IsActive] = @p0", q.Sql);
    }

    [Fact]
    public void Field_to_field_comparison_does_not_create_parameter()
    {
        var q = Compile(Def("employee", LogicalOperator.And,
            Cond("c1", "endDate", ComparisonOperator.Lt, ValueSpec.OfField("startDate"))));

        Assert.Contains("t0.[EndDate] < t0.[StartDate]", q.Sql);
        Assert.Empty(q.Parameters);
        // שני השדות מוצגים בתוצאה כ"ראיות"
        Assert.NotNull(q.AliasForField("endDate"));
        Assert.NotNull(q.AliasForField("startDate"));
    }

    [Fact]
    public void Related_none_becomes_not_exists_with_correlation()
    {
        var def = new ControlDefinition
        {
            RootEntity = "department",
            Filter = new GroupNode
            {
                Id = "g1",
                Children =
                {
                    Cond("c1", "isActive", ComparisonOperator.Eq, ValueSpec.Constant(true)),
                    new RelatedNode
                    {
                        Id = "r1", Relation = "employees", Quantifier = Quantifier.None,
                        Filter = new GroupNode { Id = "g2", Children = { Cond("c2", "isActive", ComparisonOperator.Eq, ValueSpec.Constant(true)) } }
                    }
                }
            }
        };

        var q = Compile(def);

        Assert.Contains("(NOT EXISTS (SELECT 1 FROM [hr].[vw_EmployeesEnriched] AS t1 WHERE t1.[DepartmentId] = t0.[DepartmentId] AND (t1.[IsActive] = @p1)))", q.Sql);
        // דגלים נוצרים רק לצמתים ברמה הראשית
        Assert.NotNull(q.AliasForNode("r1"));
        Assert.Null(q.AliasForNode("c2"));
    }

    [Fact]
    public void Related_any_with_inner_relation_path_joins_inside_subquery()
    {
        var def = Def("employee", LogicalOperator.And,
            new RelatedNode
            {
                Id = "r1", Relation = "assignments", Quantifier = Quantifier.Any,
                Filter = new GroupNode { Id = "g2", Children = { Cond("c2", "project.status", ComparisonOperator.Eq, ValueSpec.Constant("הסתיים")) } }
            });

        var q = Compile(def);

        Assert.Contains("EXISTS (SELECT 1 FROM [hr].[EmployeeProjects] AS t1 LEFT JOIN [hr].[Projects] AS t2 ON t2.[ProjectId] = t1.[ProjectId] WHERE t1.[EmployeeId] = t0.[EmployeeId] AND (t2.[Status] = @p0))", q.Sql);
    }

    [Fact]
    public void Neq_and_not_in_include_nulls()
    {
        var q = Compile(Def("employee", LogicalOperator.And,
            Cond("c1", "email", ComparisonOperator.Neq, ValueSpec.Constant("a")),
            Cond("c2", "age", ComparisonOperator.NotIn, ValueSpec.ListOf(1, 2, 3))));

        Assert.Contains("(t0.[Email] <> @p0 OR t0.[Email] IS NULL)", q.Sql);
        Assert.Contains("(t0.[Age] NOT IN (@p1, @p2, @p3) OR t0.[Age] IS NULL)", q.Sql);
    }

    [Fact]
    public void Like_wildcards_in_user_value_are_escaped()
    {
        var q = Compile(Def("employee", LogicalOperator.And,
            Cond("c1", "email", ComparisonOperator.Contains, ValueSpec.Constant("50%_off"))));

        Assert.Contains("t0.[Email] LIKE @p0 ESCAPE '\\'", q.Sql);
        Assert.Equal("%50\\%\\_off%", q.Parameters[0].Value);
    }

    [Fact]
    public void Today_value_is_computed_on_server()
    {
        var q = Compile(Def("employee", LogicalOperator.And,
            Cond("c1", "startDate", ComparisonOperator.Gt, ValueSpec.TodayPlus(-30))));

        Assert.Contains("t0.[StartDate] > DATEADD(DAY, -30, CAST(GETDATE() AS DATE))", q.Sql);
    }

    [Fact]
    public void Negated_group_and_between()
    {
        var def = Def("employee", LogicalOperator.And,
            new GroupNode
            {
                Id = "g2", Negate = true, Logic = LogicalOperator.Or,
                Children = { Cond("c1", "age", ComparisonOperator.Between, ValueSpec.Constant(18), ValueSpec.Constant(67)) }
            });

        var q = Compile(def);

        Assert.Contains("(NOT ((t0.[Age] BETWEEN @p0 AND @p1)))", q.Sql);
    }

    [Fact]
    public void Output_contains_key_title_display_and_flags()
    {
        var q = Compile(Def("employee", LogicalOperator.And,
            Cond("c1", "age", ComparisonOperator.Lt, ValueSpec.Constant(18))));

        Assert.Equal("__key", q.KeyAlias);
        Assert.Contains(q.Columns, c => c.Role == ColumnRole.Title && c.FieldPath == "fullName");
        Assert.Contains(q.Columns, c => c.Role == ColumnRole.Display && c.FieldPath == "isActive");
        Assert.Contains(q.Columns, c => c.Role == ColumnRole.Evidence && c.FieldPath == "age");
        Assert.Contains("CASE WHEN t0.[Age] < @p0 THEN 1 ELSE 0 END AS [__f_c1]", q.Sql);
        Assert.Contains("ORDER BY t0.[EmployeeId];", q.Sql);
    }

    [Fact]
    public void Generated_sql_is_select_only()
    {
        var q = Compile(Def("employee", LogicalOperator.And,
            Cond("c1", "age", ComparisonOperator.Lt, ValueSpec.Constant(18))));

        SqlQueryExecutor.EnsureReadOnly(q.Sql); // לא זורק
        Assert.Throws<InvalidOperationException>(() => SqlQueryExecutor.EnsureReadOnly("DELETE FROM hr.Employees"));
        Assert.Throws<InvalidOperationException>(() => SqlQueryExecutor.EnsureReadOnly("SELECT 1; DROP TABLE x"));
    }

    [Theory]
    [InlineData("Employees]; DROP TABLE x;--")]
    [InlineData("a b")]
    [InlineData("")]
    public void Unsafe_identifiers_are_rejected(string name)
    {
        Assert.False(SqlIdentifier.IsSafe(name));
        Assert.Throws<InvalidOperationException>(() => SqlIdentifier.Quote(name));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
