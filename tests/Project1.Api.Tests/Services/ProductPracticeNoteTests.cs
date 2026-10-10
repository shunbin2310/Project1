using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.Products;
using Project1.Api.Entities;
using Project1.Api.Services.Products;

namespace Project1.Api.Tests.Services;

public sealed class ProductPracticeNoteTests
{
    [Fact]
    public async Task MaximumLengthPracticeNote_RoundTripsThroughService()
    {
        var note = new string('x', 100);
        await CreateAndUpdate_RoundTripNormalizedPracticeNote(note, note);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Deployment practice  ", "Deployment practice")]
    public async Task CreateAndUpdate_RoundTripNormalizedPracticeNote(string? input, string? expected)
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
            CicdPracticeNote = input
        }, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.Success, created.Status);
        Assert.NotNull(created.Product);
        var id = created.Product.Id;
        Assert.Equal(expected, created.Product.CicdPracticeNote);
        context.ChangeTracker.Clear();
        Assert.Equal(expected, (await service.GetByIdAsync(id, CancellationToken.None))!.CicdPracticeNote);
        Assert.Equal(expected, Assert.Single(await service.GetAllAsync(false, CancellationToken.None)).CicdPracticeNote);

        var stored = await context.Products.SingleAsync();
        stored.Note = "Keep the existing business note";
        stored.CicdPracticeNote = "Replace this practice value";
        await context.SaveChangesAsync();
        var updated = await service.UpdateAsync(id, new UpdateProductRequest
        {
            Name = "Practice product",
            ProductCategoryId = category.Id,
            UnitOfMeasureId = unit.Id,
            CicdPracticeNote = input,
            IsActive = true
        }, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.Success, updated.Status);
        Assert.Equal(expected, updated.Product!.CicdPracticeNote);
        context.ChangeTracker.Clear();
        stored = await context.Products.SingleAsync();
        Assert.Equal(expected, stored.CicdPracticeNote);
        Assert.Equal("Keep the existing business note", stored.Note);

        var deactivated = await service.DeactivateAsync(id, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.Success, deactivated.Status);
        Assert.False(deactivated.Product!.IsActive);
        Assert.Equal(expected, deactivated.Product.CicdPracticeNote);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Requests_ValidateOptionalPracticeNoteLength(int? length, bool expectedValid)
    {
        var note = length.HasValue ? new string('x', length.Value) : null;
        object[] requests =
        [
            new CreateProductRequest { Name = "Practice product", ProductCategoryId = 1, UnitOfMeasureId = 1, CicdPracticeNote = note },
            new UpdateProductRequest { Name = "Practice product", ProductCategoryId = 1, UnitOfMeasureId = 1, CicdPracticeNote = note }
        ];
        foreach (var request in requests)
        {
            var errors = new List<ValidationResult>();
            Assert.Equal(expectedValid, Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
            if (!expectedValid)
                Assert.Contains(errors, error => error.MemberNames.Contains(nameof(CreateProductRequest.CicdPracticeNote)));
        }
    }
}
