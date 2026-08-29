using Microsoft.EntityFrameworkCore;
using Project1.Api.Entities;

namespace Project1.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<Product> Products => Set<Product>();

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

        modelBuilder.Entity<ProductCategory>(entity =>
        {
            entity.ToTable("ProductCategories");

            entity.HasKey(category => category.Id);

            entity.Property(category => category.Code)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(category => category.Code)
                .IsUnique();

            entity.Property(category => category.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(category => category.Name)
                .IsUnique();

            entity.Property(category => category.Description)
                .HasMaxLength(500);
        });

        modelBuilder.Entity<UnitOfMeasure>(entity =>
        {
            entity.ToTable("UnitsOfMeasure");

            entity.HasKey(unit => unit.Id);

            entity.Property(unit => unit.Code)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(unit => unit.Code)
                .IsUnique();

            entity.Property(unit => unit.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(unit => unit.Description)
                .HasMaxLength(500);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");

            entity.HasKey(product => product.Id);

            entity.Property(product => product.Code)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(product => product.Code)
                .IsUnique();

            entity.Property(product => product.Name)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(product => product.Description)
                .HasMaxLength(500);

            entity.Property(product => product.DefaultUnitPrice)
                .HasPrecision(18, 2);

            entity.Property(product => product.ReorderLevel)
                .HasPrecision(18, 3);

            entity.HasOne(product => product.ProductCategory)
                .WithMany(category => category.Products)
                .HasForeignKey(product => product.ProductCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(product => product.UnitOfMeasure)
                .WithMany(unit => unit.Products)
                .HasForeignKey(product => product.UnitOfMeasureId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
