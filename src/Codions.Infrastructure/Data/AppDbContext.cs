using Microsoft.EntityFrameworkCore;
using Codions.Contracts.Interfaces;

namespace Codions.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<JobEntity> Jobs => Set<JobEntity>();
    public DbSet<JobArtifactEntity> JobArtifacts => Set<JobArtifactEntity>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobEntity>(entity =>
        {
            entity.ToTable("Jobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.RequesterJson).IsRequired();
            entity.Property(e => e.RepoJson).IsRequired();
            entity.Property(e => e.TaskJson).IsRequired();
            entity.Property(e => e.RunProfileJson).IsRequired();
            entity.Property(e => e.BranchName).HasMaxLength(200);
            entity.Property(e => e.PrUrl).HasMaxLength(500);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedUtc);
        });

        modelBuilder.Entity<JobArtifactEntity>(entity =>
        {
            entity.ToTable("JobArtifacts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.ArtifactType).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.FilePath).HasMaxLength(500);

            entity.HasIndex(e => new { e.JobId, e.ArtifactType });
        });
    }
}

public sealed class JobArtifactEntity
{
    public int Id { get; set; }
    public required string JobId { get; set; }
    public required string ArtifactType { get; set; }
    public required string FilePath { get; set; }
    public required DateTime CreatedUtc { get; set; }
}