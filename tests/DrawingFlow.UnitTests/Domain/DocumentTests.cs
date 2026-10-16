using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;
using DrawingFlow.Domain.Workflow;

namespace DrawingFlow.UnitTests.Domain;

public sealed class DocumentTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static Document NewDocument() => Document.Create(Guid.NewGuid(), "  АР-01.2  КЖ ", "План фундаментов", null, "КЖ", "Чертёж", Guid.NewGuid(), Now);

    private static DocumentVersion AddVersion(Document document, int number, string? comment = null)
    {
        var version = document.RegisterVersion(Guid.NewGuid(), number, "C:\\Проекты\\план.dwg", $"2026/10/{document.Id:N}/{Guid.NewGuid():N}.dwg", "application/acad", 1024, Hash.ToUpperInvariant(), Guid.NewGuid(), null, comment, $"key-{number}", Now.AddMinutes(number));
        document.MakeCurrent(version);
        return version;
    }

    [Fact]
    public void Create_normalizes_number_and_starts_as_draft()
    {
        var document = NewDocument();
        Assert.Equal("АР-01.2  КЖ", document.DocumentNumber);
        Assert.Equal("АР-01.2 КЖ", document.NormalizedDocumentNumber);
        Assert.Equal(DocumentStatus.Draft, document.Status);
        Assert.Null(document.CurrentVersionId);
    }

    [Fact]
    public void RegisterVersion_sets_current_version_and_moves_to_in_progress()
    {
        var document = NewDocument();
        var version = AddVersion(document, 1, "Первая выдача");

        Assert.Equal(DocumentStatus.InProgress, document.Status);
        Assert.Equal(version.Id, document.CurrentVersionId);
        Assert.Equal("план.dwg", version.OriginalFileName);
        Assert.Equal(Hash, version.Sha256Hash);
        Assert.Equal(VersionStatus.Registered, version.Status);
        Assert.Equal(Now.AddMinutes(1), document.UpdatedAtUtc);
    }

    [Fact]
    public void New_version_never_inherits_approval()
    {
        var document = NewDocument();
        AddVersion(document, 1);
        document.TransitionTo(DocumentStatus.InReview, Now);
        document.TransitionTo(DocumentStatus.Approved, Now);
        document.TransitionTo(DocumentStatus.Released, Now);

        var second = AddVersion(document, 2);

        Assert.Equal(DocumentStatus.InProgress, document.Status);
        Assert.Equal(second.Id, document.CurrentVersionId);
        Assert.Equal(2, document.Versions.Count);
    }

    [Fact]
    public void Archived_document_rejects_new_versions_until_restored()
    {
        var document = NewDocument();
        AddVersion(document, 1);
        document.Archive(Now);
        Assert.Throws<DomainRuleException>(() => AddVersion(document, 2));

        document.Restore(Now);
        Assert.Equal(DocumentStatus.InProgress, document.Status);
        AddVersion(document, 2);
    }

    [Fact]
    public void Restore_without_versions_returns_to_draft()
    {
        var document = NewDocument();
        document.Archive(Now);
        document.Restore(Now);
        Assert.Equal(DocumentStatus.Draft, document.Status);
    }

    [Fact]
    public void MakeCurrent_rejects_foreign_version()
    {
        var document = NewDocument();
        var other = NewDocument();
        var foreign = AddVersion(other, 1);
        Assert.Throws<ArgumentException>(() => document.MakeCurrent(foreign));
    }

    [Fact]
    public void Version_id_is_required_and_used_as_entity_key()
    {
        var document = NewDocument();
        var id = Guid.NewGuid();
        var version = document.RegisterVersion(id, 1, "a.pdf", "k", null, 1, Hash, Guid.NewGuid(), null, null, "key", Now);
        Assert.Equal(id, version.Id);
        Assert.Throws<ArgumentException>(() => document.RegisterVersion(Guid.Empty, 2, "a.pdf", "k2", null, 1, Hash, Guid.NewGuid(), null, null, "key2", Now));
    }

    [Fact]
    public void Duplicate_version_number_is_a_conflict()
    {
        var document = NewDocument();
        AddVersion(document, 1);
        Assert.Throws<ConflictException>(() => AddVersion(document, 1));
    }

    [Fact]
    public void Version_requires_valid_hash_file_name_and_author()
    {
        var document = NewDocument();
        Assert.Throws<ValidationException>(() => document.RegisterVersion(Guid.NewGuid(), 1, "a.pdf", "k", null, 1, "not-a-hash", Guid.NewGuid(), null, null, "key", Now));
        Assert.Throws<ValidationException>(() => document.RegisterVersion(Guid.NewGuid(), 1, "...", "k", null, 1, Hash, Guid.NewGuid(), null, null, "key", Now));
        Assert.Throws<ValidationException>(() => document.RegisterVersion(Guid.NewGuid(), 1, "a.pdf", "k", null, -1, Hash, Guid.NewGuid(), null, null, "key", Now));
        Assert.Throws<ArgumentException>(() => document.RegisterVersion(Guid.NewGuid(), 1, "a.pdf", "k", null, 1, Hash, null, null, null, "key", Now));
        Assert.Throws<ValidationException>(() => document.RegisterVersion(Guid.NewGuid(), 1, "a.pdf", "k", null, 1, Hash, Guid.NewGuid(), null, null, " ", Now));
    }

    [Theory]
    [InlineData(DocumentStatus.Draft, DocumentStatus.InReview, false)]
    [InlineData(DocumentStatus.InProgress, DocumentStatus.InReview, true)]
    [InlineData(DocumentStatus.InReview, DocumentStatus.Approved, true)]
    [InlineData(DocumentStatus.InReview, DocumentStatus.Released, false)]
    [InlineData(DocumentStatus.Approved, DocumentStatus.Released, true)]
    [InlineData(DocumentStatus.Released, DocumentStatus.Approved, false)]
    [InlineData(DocumentStatus.RevisionRequired, DocumentStatus.Approved, false)]
    [InlineData(DocumentStatus.Archived, DocumentStatus.Released, false)]
    public void Workflow_transition_table(DocumentStatus from, DocumentStatus to, bool allowed)
    {
        Assert.Equal(allowed, DocumentWorkflow.CanTransition(from, to));
        if (!allowed)
        {
            Assert.Throws<DomainRuleException>(() => DocumentWorkflow.EnsureTransition(from, to));
        }
    }

    [Fact]
    public void Invalid_transition_message_is_russian()
    {
        var ex = Assert.Throws<DomainRuleException>(() => DocumentWorkflow.EnsureTransition(DocumentStatus.Draft, DocumentStatus.Released));
        Assert.Contains("Черновик", ex.Message);
        Assert.Contains("Выпущен", ex.Message);
    }
}
