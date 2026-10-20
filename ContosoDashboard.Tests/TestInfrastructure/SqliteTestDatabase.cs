using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests.TestInfrastructure;

public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public ApplicationDbContext Context { get; }

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        Context = new ApplicationDbContext(options);
        Context.Database.EnsureCreated();
    }

    public async Task<User> AddUserAsync(string name, UserRole role, string department)
    {
        var user = new User
        {
            Email = $"{Guid.NewGuid():N}@contoso.test",
            DisplayName = name,
            Role = role,
            Department = department,
            CreatedDate = DateTime.UtcNow
        };
        Context.Users.Add(user);
        await Context.SaveChangesAsync();
        return user;
    }

    public async Task<Project> AddProjectAsync(int managerId, string name = "Test Project")
    {
        var project = new Project
        {
            Name = name,
            ProjectManagerId = managerId,
            StartDate = DateTime.UtcNow,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow,
            Status = ProjectStatus.Active
        };
        Context.Projects.Add(project);
        await Context.SaveChangesAsync();
        return project;
    }

    public async Task AddProjectMemberAsync(int projectId, int userId)
    {
        Context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = "TeamMember",
            AssignedDate = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}