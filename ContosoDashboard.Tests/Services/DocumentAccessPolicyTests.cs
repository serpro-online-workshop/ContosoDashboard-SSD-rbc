using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestInfrastructure;

namespace ContosoDashboard.Tests.Services;

public class DocumentAccessPolicyTests
{
    [Fact]
    public async Task EvaluateAsync_AppliesOwnerProjectManagerTeamLeadAndAdminRules()
    {
        await using var database = new SqliteTestDatabase();
        var document = new Document
        {
            Title = "Private document",
            Category = "Personal Files",
            OriginalFileName = "private.pdf",
            StoragePath = "4/personal/private.pdf",
            FileType = "application/pdf",
            FileSizeBytes = 10,
            UploadedByUserId = 4,
            ProjectId = 1
        };
        database.Context.Documents.Add(document);
        await database.Context.SaveChangesAsync();
        var policy = new DocumentAccessPolicy(database.Context);

        var owner = await policy.EvaluateAsync(4, document.DocumentId);
        var teamLead = await policy.EvaluateAsync(3, document.DocumentId);
        var projectManager = await policy.EvaluateAsync(2, document.DocumentId);
        var admin = await policy.EvaluateAsync(1, document.DocumentId);

        Assert.True(owner!.CanRead && owner.CanEdit && owner.CanChangeProject && owner.CanReplace && owner.CanDelete && owner.CanShare);
        Assert.True(teamLead!.CanRead && teamLead.CanEdit && teamLead.CanDelete);
        Assert.False(teamLead.CanChangeProject || teamLead.CanReplace || teamLead.CanShare);
        Assert.True(projectManager!.CanRead && projectManager.CanDelete);
        Assert.False(projectManager.CanEdit || projectManager.CanChangeProject || projectManager.CanReplace || projectManager.CanShare);
        Assert.True(admin!.CanRead && admin.CanDelete);
        Assert.False(admin.CanEdit || admin.CanChangeProject || admin.CanReplace || admin.CanShare);
    }

    [Fact]
    public async Task EvaluateAsync_ProjectMemberReadsButDoesNotManageAndMissingReturnsNull()
    {
        await using var database = new SqliteTestDatabase();
        var employee = await database.AddUserAsync("Project employee", UserRole.Employee, "Engineering");
        await database.AddProjectMemberAsync(1, employee.UserId);
        var document = new Document
        {
            Title = "Project document",
            Category = "Project Documents",
            OriginalFileName = "project.pdf",
            StoragePath = "4/1/project.pdf",
            FileType = "application/pdf",
            FileSizeBytes = 10,
            UploadedByUserId = 4,
            ProjectId = 1
        };
        database.Context.Documents.Add(document);
        await database.Context.SaveChangesAsync();
        var policy = new DocumentAccessPolicy(database.Context);

        var member = await policy.EvaluateAsync(employee.UserId, document.DocumentId);
        var missing = await policy.EvaluateAsync(employee.UserId, int.MaxValue);

        Assert.True(member!.CanRead);
        Assert.False(member.CanEdit || member.CanChangeProject || member.CanReplace || member.CanDelete || member.CanShare);
        Assert.Null(missing);
    }

    [Fact]
    public async Task EvaluateAsync_DeniesOtherDepartmentTeamLeadAndUnrelatedUser()
    {
        await using var database = new SqliteTestDatabase();
        var otherLead = await database.AddUserAsync("Other lead", UserRole.TeamLead, "Finance");
        var unrelated = await database.AddUserAsync("Unrelated", UserRole.Employee, "Finance");
        var document = new Document
        {
            Title = "Personal document",
            Category = "Personal Files",
            OriginalFileName = "private.pdf",
            StoragePath = "4/personal/private.pdf",
            FileType = "application/pdf",
            FileSizeBytes = 10,
            UploadedByUserId = 4
        };
        database.Context.Documents.Add(document);
        await database.Context.SaveChangesAsync();
        var policy = new DocumentAccessPolicy(database.Context);

        Assert.False((await policy.EvaluateAsync(otherLead.UserId, document.DocumentId))!.CanRead);
        Assert.False((await policy.EvaluateAsync(unrelated.UserId, document.DocumentId))!.CanRead);
    }

    [Fact]
    public async Task EvaluateAsync_ShareGrantsReadOnlyAccess()
    {
        await using var database = new SqliteTestDatabase();
        var document = new Document
        {
            Title = "Shared document",
            Category = "Personal Files",
            OriginalFileName = "shared.pdf",
            StoragePath = "4/personal/shared.pdf",
            FileType = "application/pdf",
            FileSizeBytes = 10,
            UploadedByUserId = 4
        };
        database.Context.Documents.Add(document);
        await database.Context.SaveChangesAsync();
        database.Context.DocumentShares.Add(new DocumentShare
        {
            DocumentId = document.DocumentId,
            SharedWithUserId = 2,
            SharedByUserId = 4
        });
        await database.Context.SaveChangesAsync();
        var policy = new DocumentAccessPolicy(database.Context);

        var sharee = await policy.EvaluateAsync(2, document.DocumentId);

        Assert.True(sharee!.CanRead);
        Assert.False(sharee.CanEdit || sharee.CanChangeProject || sharee.CanReplace || sharee.CanDelete || sharee.CanShare);
    }

    [Fact]
    public async Task EvaluateAsync_DepartmentShareGrantsReadOnlyAccessAndProjectRemovalPreservesOwnerAccess()
    {
        await using var database = new SqliteTestDatabase();
        var financeUser = await database.AddUserAsync("Finance user", UserRole.Employee, "Finance");
        var document = new Document
        {
            Title = "Project document",
            Category = "Project Documents",
            OriginalFileName = "project.pdf",
            StoragePath = "4/1/project.pdf",
            FileType = "application/pdf",
            FileSizeBytes = 10,
            UploadedByUserId = 4,
            ProjectId = 1
        };
        database.Context.Documents.Add(document);
        await database.Context.SaveChangesAsync();
        database.Context.ProjectMembers.RemoveRange(database.Context.ProjectMembers.Where(member => member.UserId == 4));
        database.Context.DocumentShares.Add(new DocumentShare
        {
            DocumentId = document.DocumentId,
            SharedWithDepartment = "Finance",
            SharedByUserId = 4
        });
        await database.Context.SaveChangesAsync();
        var policy = new DocumentAccessPolicy(database.Context);

        var owner = await policy.EvaluateAsync(4, document.DocumentId);
        var departmentSharee = await policy.EvaluateAsync(financeUser.UserId, document.DocumentId);

        Assert.True(owner!.CanRead);
        Assert.True(departmentSharee!.CanRead);
        Assert.False(departmentSharee.CanEdit || departmentSharee.CanChangeProject || departmentSharee.CanReplace || departmentSharee.CanDelete || departmentSharee.CanShare);
    }
}