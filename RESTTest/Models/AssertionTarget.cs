namespace RESTTest.Models;

/// <summary>What part of a response an assertion looks at.</summary>
public enum AssertionTarget
{
    StatusCode = 0,
    ResponseTimeMs = 1,
    Header = 2,
    BodyText = 3,
    JsonPath = 4,
}
