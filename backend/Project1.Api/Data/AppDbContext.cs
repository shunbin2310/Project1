using Microsoft.EntityFrameworkCore;
using Project1.Api.Entities;

namespace Project1.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

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

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.ToTable("Suppliers");

            entity.HasKey(supplier => supplier.Id);

            entity.Property(supplier => supplier.Code)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(supplier => supplier.Code)
                .IsUnique();

            entity.Property(supplier => supplier.Name)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(supplier => supplier.ContactPerson)
                .HasMaxLength(100);

            entity.Property(supplier => supplier.Email)
                .HasMaxLength(254);

            entity.Property(supplier => supplier.Phone)
                .HasMaxLength(30);

            entity.Property(supplier => supplier.Address)
                .HasMaxLength(500);
        });
    }
}
