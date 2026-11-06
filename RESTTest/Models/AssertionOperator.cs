namespace RESTTest.Models;

/// <summary>
/// How the actual value is compared with the expected one. Named <c>EqualTo</c> rather than
/// <c>Equals</c> because an enum member called <c>Equals</c> hides <see cref="object.Equals(object?)"/>.
/// </summary>
public enum AssertionOperator
{
    EqualTo = 0,
    NotEqualTo = 1,
    Contains = 2,
    NotContains = 3,
    Exists = 4,
    LessThan = 5,
    GreaterThan = 6,
}
