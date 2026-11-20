namespace JobRadar.Contracts;

/// <summary>Level read from a title or a feed's level field. <see cref="Unknown"/> when nothing says.</summary>
public enum Seniority
{
    Unknown = 0,
    Intern = 1,
    Junior = 2,
    Mid = 3,
    Senior = 4,
    Lead = 5,
}
