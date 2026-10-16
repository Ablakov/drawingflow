using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.UnitTests.Domain;

public sealed class WorkflowCommandTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static Document NewDocument() => Document.Create(Guid.NewGuid(), "АР-01", "План", null, null, null, Guid.NewGuid(), Now);

    private static DocumentVersion AddVersion(Document document, int number)
    {
        var version = document.RegisterVersion(Guid.NewGuid(), number, "план.dwg", $"k/{number}", null, 10, Hash, Guid.NewGuid(), null, null, $"key-{number}", Now);
        document.MakeCurrent(version);
        return version;
    }

    [Fact]
    public void Full_cycle_submit_approve_release()
    {
        var document = NewDocument();
        var v1 = AddVersion(document, 1);

        document.SubmitForReview(v1, Now);
        Assert.Equal(DocumentStatus.InReview, document.Status);
        Assert.Equal(VersionStatus.InReview, v1.Status);

        var review = document.Approve(v1, Reviewer, "Замечаний нет", Now.AddHours(1));
        Assert.Equal(ReviewDecision.Approved, review.Decision);
        Assert.Equal(DocumentStatus.Approved, document.Status);
        Assert.Equal(VersionStatus.Approved, v1.Status);

        document.Release(v1, Now.AddHours(2));
        Assert.Equal(DocumentStatus.Released, document.Status);
        Assert.Equal(VersionStatus.Released, v1.Status);
    }

    [Fact]
    public void Revision_requires_comment_and_new_version_restarts_cycle()
    {
        var document = NewDocument();
        var v1 = AddVersion(document, 1);
        document.SubmitForReview(v1, Now);

        var noComment = Assert.Throws<ValidationException>(() => document.RequestRevision(v1, Reviewer, "  ", Now));
        Assert.True(noComment.Errors.ContainsKey("comment"));
        Assert.Equal(DocumentStatus.InReview, document.Status); // неудачная попытка не меняет состояние

        var review = document.RequestRevision(v1, Reviewer, "Исправить отметки", Now);
        Assert.Equal(DocumentStatus.RevisionRequired, document.Status);
        Assert.Equal(VersionStatus.RevisionRequired, v1.Status);
        Assert.Equal("Исправить отметки", review.Comment);

        // Повторная отправка той же версии невозможна — нужна новая версия.
        Assert.Throws<DomainRuleException>(() => document.SubmitForReview(v1, Now));

        var v2 = AddVersion(document, 2);
        Assert.True(v1.Supersede());
        Assert.Equal(VersionStatus.Superseded, v1.Status);
        Assert.Equal(DocumentStatus.InProgress, document.Status);
        document.SubmitForReview(v2, Now);
        Assert.Equal(DocumentStatus.InReview, document.Status);
    }

    [Fact]
    public void Approval_is_never_inherited_and_history_is_preserved()
    {
        var document = NewDocument();
        var v1 = AddVersion(document, 1);
        document.SubmitForReview(v1, Now);
        document.Approve(v1, Reviewer, null, Now);
        document.Release(v1, Now);

        var v2 = AddVersion(document, 2);
        Assert.Equal(DocumentStatus.InProgress, document.Status);
        Assert.Equal(VersionStatus.Registered, v2.Status);
        Assert.False(v1.Supersede());
        Assert.Equal(VersionStatus.Released, v1.Status); // выпущенная версия остаётся в истории

        // Выпустить несогласованную версию нельзя; решения — только по текущей версии.
        Assert.Throws<DomainRuleException>(() => document.Release(v2, Now));
        Assert.Throws<DomainRuleException>(() => document.Approve(v1, Reviewer, null, Now));
        Assert.Throws<DomainRuleException>(() => document.SubmitForReview(v1, Now));
    }

    [Fact]
    public void Decisions_require_review_state()
    {
        var document = NewDocument();
        var v1 = AddVersion(document, 1);
        Assert.Throws<DomainRuleException>(() => document.Approve(v1, Reviewer, null, Now));
        Assert.Throws<DomainRuleException>(() => document.RequestRevision(v1, Reviewer, "x", Now));
        Assert.Throws<DomainRuleException>(() => document.Release(v1, Now));

        document.Archive(Now);
        Assert.Throws<DomainRuleException>(() => document.SubmitForReview(v1, Now));
        Assert.Throws<DomainRuleException>(() => NewDocument().SubmitForReview(v1, Now)); // чужая версия
    }

    [Fact]
    public void Task_lifecycle()
    {
        var task = WorkflowTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), WorkflowTaskType.Review, Guid.NewGuid(), Now.AddDays(3), "Проверить", Now);
        Assert.True(task.IsOpen);
        Assert.Equal(Now.AddDays(3), task.DueAtUtc);

        task.Complete(Reviewer, Now.AddHours(1), null);
        Assert.Equal(WorkflowTaskStatus.Completed, task.Status);
        Assert.Equal("Проверить", task.Comment);
        Assert.Throws<DomainRuleException>(() => task.Cancel(null, Now, "x"));

        var other = WorkflowTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), WorkflowTaskType.Rework, null, null, null, Now);
        other.Cancel(null, Now, "Загружена новая версия");
        Assert.Equal(WorkflowTaskStatus.Cancelled, other.Status);
        Assert.Equal("Загружена новая версия", other.Comment);

        Assert.Throws<ValidationException>(() => WorkflowTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), WorkflowTaskType.Review, null, Now.AddDays(-1), null, Now));
        Assert.Throws<ArgumentException>(() => WorkflowTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), WorkflowTaskType.Review, null, DateTime.Now.AddDays(1), null, Now));
    }
}
