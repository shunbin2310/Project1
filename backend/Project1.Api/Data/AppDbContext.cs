using Microsoft.EntityFrameworkCore;
using Project1.Api.Entities;

namespace Project1.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Department> Departments => Set<Department>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Departments");

            entity.HasKey(department => department.Id);

            entity.Property(department => department.Code)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(department => department.Code)
                .IsUnique();

            entity.Property(department => department.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(department => department.Description)
                .HasMaxLength(500);

        });
    }
}
