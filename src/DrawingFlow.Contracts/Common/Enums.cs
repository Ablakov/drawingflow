namespace DrawingFlow.Contracts;

// Значения намеренно совпадают с перечислениями DrawingFlow.Domain.Enums (проверяется тестом на паритет).
// Контракты не ссылаются на домен, чтобы клиенты (Desktop, Agent) не зависели от серверной сборки.

public enum OrganizationRole
{
    Member = 0,
    OrganizationAdmin = 1,
    Owner = 2,
}

public enum ProjectRole
{
    Viewer = 0,
    Reviewer = 1,
    Engineer = 2,
    ProjectManager = 3,
}

public enum ProjectStatus
{
    Active = 0,
    Archived = 1,
}
