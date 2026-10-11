using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.Products;
using Project1.Api.Entities;
using Project1.Api.Services.Products;

namespace Project1.Api.Tests.Services;

public sealed class ProductStorageLocationTests
{
    [Fact]
    public async Task MaximumLengthStorageLocation_RoundTripsThroughService()
    {
        var note = new string('x', 100);
        await CreateAndUpdate_RoundTripNormalizedStorageLocation(note, note);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Shelf A1  ", "Shelf A1")]
    public async Task CreateAndUpdate_RoundTripNormalizedStorageLocation(string? input, string? expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var category = new ProductCategory { Code = "CI-CATEGORY", Name = "Practice category" };
        var unit = new UnitOfMeasure { Code = "CI-UNIT", Name = "Practice unit" };
        context.AddRange(category, unit);
        await context.SaveChangesAsync();
        var service = new ProductService(context);

        var created = await service.CreateAsync(new CreateProductRequest
        {
            Name = "Practice product",
            ProductCategoryId = category.Id,
            UnitOfMeasureId = unit.Id,
            StorageLocation = input
        }, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.Success, created.Status);
        Assert.NotNull(created.Product);
        var id = created.Product.Id;
        Assert.Equal(expected, created.Product.StorageLocation);
        context.ChangeTracker.Clear();
        Assert.Equal(expected, (await service.GetByIdAsync(id, CancellationToken.None))!.StorageLocation);
        Assert.Equal(expected, Assert.Single(await service.GetAllAsync(false, CancellationToken.None)).StorageLocation);

        var stored = await context.Products.SingleAsync();
        stored.Note = "Keep the existing business note";
        stored.StorageLocation = "Replace this location";
        await context.SaveChangesAsync();
        var updated = await service.UpdateAsync(id, new UpdateProductRequest
        {
            Name = "Practice product",
            ProductCategoryId = category.Id,
            UnitOfMeasureId = unit.Id,
            StorageLocation = input,
            IsActive = true
        }, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.Success, updated.Status);
        Assert.Equal(expected, updated.Product!.StorageLocation);
        context.ChangeTracker.Clear();
        stored = await context.Products.SingleAsync();
        Assert.Equal(expected, stored.StorageLocation);
        Assert.Equal("Keep the existing business note", stored.Note);

        var deactivated = await service.DeactivateAsync(id, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.Success, deactivated.Status);
        Assert.False(deactivated.Product!.IsActive);
        Assert.Equal(expected, deactivated.Product.StorageLocation);

        var reactivated = await service.UpdateAsync(id, new UpdateProductRequest
        {
            Name = "Practice product",
            ProductCategoryId = category.Id,
            UnitOfMeasureId = unit.Id,
            StorageLocation = expected,
            IsActive = true
        }, CancellationToken.None);
        Assert.True(reactivated.Product!.IsActive);
        Assert.Equal(expected, reactivated.Product.StorageLocation);

        // Clearing the field persists null without touching the business note.
        var cleared = await service.UpdateAsync(id, new UpdateProductRequest
        {
            Name = "Practice product",
            ProductCategoryId = category.Id,
            UnitOfMeasureId = unit.Id,
            StorageLocation = "   ",
            IsActive = true
        }, CancellationToken.None);
        Assert.Null(cleared.Product!.StorageLocation);
        context.ChangeTracker.Clear();
        stored = await context.Products.SingleAsync();
        Assert.Null(stored.StorageLocation);
        Assert.Equal("Keep the existing business note", stored.Note);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Requests_ValidateOptionalStorageLocationLength(int? length, bool expectedValid)
    {
        var note = length.HasValue ? new string('x', length.Value) : null;
        object[] requests =
        [
            new CreateProductRequest { Name = "Practice product", ProductCategoryId = 1, UnitOfMeasureId = 1, StorageLocation = note },
            new UpdateProductRequest { Name = "Practice product", ProductCategoryId = 1, UnitOfMeasureId = 1, StorageLocation = note }
        ];
        foreach (var request in requests)
        {
            var errors = new List<ValidationResult>();
            Assert.Equal(expectedValid, Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
            if (!expectedValid)
                Assert.Contains(errors, error => error.MemberNames.Contains(nameof(CreateProductRequest.StorageLocation)));
        }
    }
}
