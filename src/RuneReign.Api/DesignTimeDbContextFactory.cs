using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RuneReign.Api;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RuneDbContext>
{
    public RuneDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RuneDbContext>()
            .UseMySql("Server=localhost;Database=runereign;User=root;Password=development;", ServerVersion.Parse("8.0.36"))
            .Options;
        return new RuneDbContext(options);
    }
}
