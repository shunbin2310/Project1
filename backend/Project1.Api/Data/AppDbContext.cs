using Microsoft.EntityFrameworkCore;
using Project1.Api.Entities;
using Project1.Api.Entities.Workflows;

namespace Project1.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();

    public DbSet<PurchaseRequestItem> PurchaseRequestItems => Set<PurchaseRequestItem>();

    public DbSet<WorkflowProcessTemplate> WorkflowProcessTemplates => Set<WorkflowProcessTemplate>();

    public DbSet<WorkflowStepTemplate> WorkflowStepTemplates => Set<WorkflowStepTemplate>();

    public DbSet<WorkflowActionTemplate> WorkflowActionTemplates => Set<WorkflowActionTemplate>();

    public DbSet<WorkflowActionerTemplate> WorkflowActionerTemplates => Set<WorkflowActionerTemplate>();

    public DbSet<WorkflowProcessInstance> WorkflowProcessInstances => Set<WorkflowProcessInstance>();

    public DbSet<WorkflowStepInstance> WorkflowStepInstances => Set<WorkflowStepInstance>();

    public DbSet<WorkflowActionInstance> WorkflowActionInstances => Set<WorkflowActionInstance>();

    public DbSet<WorkflowActionerInstance> WorkflowActionerInstances => Set<WorkflowActionerInstance>();

    public DbSet<WorkflowHistory> WorkflowHistory => Set<WorkflowHistory>();

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

        modelBuilder.Entity<PurchaseRequest>(entity =>
        {
            entity.ToTable("PurchaseRequests");

            entity.HasKey(request => request.Id);

            entity.Property(request => request.RequestNumber)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(request => request.RequestNumber)
                .IsUnique();

            entity.Property(request => request.RequesterName)
                .HasMaxLength(100);

            entity.Property(request => request.Justification)
                .HasMaxLength(1000);

            entity.HasOne(request => request.Department)
                .WithMany()
                .HasForeignKey(request => request.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseRequestItem>(entity =>
        {
            entity.ToTable("PurchaseRequestItems");

            entity.HasKey(item => item.Id);

            entity.Property(item => item.Quantity)
                .HasPrecision(18, 3);

            entity.Property(item => item.EstimatedUnitPrice)
                .HasPrecision(18, 2);

            entity.HasIndex(item => new { item.PurchaseRequestId, item.ProductId })
                .IsUnique();

            entity.HasOne(item => item.PurchaseRequest)
                .WithMany(request => request.Items)
                .HasForeignKey(item => item.PurchaseRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowProcessTemplate>(entity =>
        {
            entity.ToTable("WorkflowProcessTemplates");

            entity.HasKey(template => template.Id);

            entity.Property(template => template.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(template => template.Name)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(template => template.EntityType)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(template => new { template.Code, template.Version })
                .IsUnique();

            entity.HasIndex(template => new
            {
                template.EntityType,
                template.IsPublished,
                template.IsActive
            });
        });

        modelBuilder.Entity<WorkflowStepTemplate>(entity =>
        {
            entity.ToTable("WorkflowStepTemplates");

            entity.HasKey(step => step.Id);

            entity.Property(step => step.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(step => step.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(step => new { step.ProcessTemplateId, step.Code })
                .IsUnique();

            entity.HasOne(step => step.ProcessTemplate)
                .WithMany(process => process.Steps)
                .HasForeignKey(step => step.ProcessTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowActionTemplate>(entity =>
        {
            entity.ToTable("WorkflowActionTemplates");

            entity.HasKey(action => action.Id);

            entity.Property(action => action.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(action => action.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(action => new { action.FromStepTemplateId, action.Code })
                .IsUnique();

            entity.HasOne(action => action.FromStepTemplate)
                .WithMany(step => step.Actions)
                .HasForeignKey(action => action.FromStepTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(action => action.ToStepTemplate)
                .WithMany()
                .HasForeignKey(action => action.ToStepTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowActionerTemplate>(entity =>
        {
            entity.ToTable("WorkflowActionerTemplates");

            entity.HasKey(actioner => actioner.Id);

            entity.Property(actioner => actioner.ActionerType)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(actioner => actioner.ActionerKey)
                .HasMaxLength(100);

            entity.HasOne(actioner => actioner.ActionTemplate)
                .WithMany(action => action.Actioners)
                .HasForeignKey(actioner => actioner.ActionTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowProcessInstance>(entity =>
        {
            entity.ToTable("WorkflowProcessInstances");

            entity.HasKey(instance => instance.Id);

            entity.Property(instance => instance.TemplateCode)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(instance => instance.TemplateName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(instance => instance.EntityType)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(instance => instance.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasIndex(instance => new { instance.EntityType, instance.EntityId })
                .IsUnique();

            entity.HasIndex(instance => new { instance.Status, instance.CurrentStepInstanceId });

            entity.HasOne(instance => instance.ProcessTemplate)
                .WithMany()
                .HasForeignKey(instance => instance.ProcessTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowStepInstance>(entity =>
        {
            entity.ToTable("WorkflowStepInstances");

            entity.HasKey(step => step.Id);

            entity.Property(step => step.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(step => step.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(step => new { step.ProcessInstanceId, step.Code })
                .IsUnique();

            entity.HasOne(step => step.ProcessInstance)
                .WithMany(instance => instance.Steps)
                .HasForeignKey(step => step.ProcessInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowActionInstance>(entity =>
        {
            entity.ToTable("WorkflowActionInstances");

            entity.HasKey(action => action.Id);

            entity.Property(action => action.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(action => action.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(action => new { action.FromStepInstanceId, action.Code })
                .IsUnique();

            entity.HasOne(action => action.FromStepInstance)
                .WithMany(step => step.Actions)
                .HasForeignKey(action => action.FromStepInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(action => action.ToStepInstance)
                .WithMany()
                .HasForeignKey(action => action.ToStepInstanceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowActionerInstance>(entity =>
        {
            entity.ToTable("WorkflowActionerInstances");

            entity.HasKey(actioner => actioner.Id);

            entity.Property(actioner => actioner.ActionerType)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(actioner => actioner.ActionerKey)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasOne(actioner => actioner.ActionInstance)
                .WithMany(action => action.Actioners)
                .HasForeignKey(actioner => actioner.ActionInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowHistory>(entity =>
        {
            entity.ToTable("WorkflowHistory");

            entity.HasKey(history => history.Id);

            entity.Property(history => history.FromStepCode)
                .HasMaxLength(50);

            entity.Property(history => history.ToStepCode)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(history => history.ActionCode)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(history => history.ActionBy)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(history => history.Comment)
                .HasMaxLength(500);

            entity.HasIndex(history => new { history.ProcessInstanceId, history.ActionAtUtc });

            entity.HasOne(history => history.ProcessInstance)
                .WithMany(instance => instance.History)
                .HasForeignKey(history => history.ProcessInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        var seedDate = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

        modelBuilder.Entity<WorkflowProcessTemplate>().HasData(new WorkflowProcessTemplate
        {
            Id = 1,
            Code = "PURCHASE_REQUEST",
            Name = "Purchase Request Approval",
            EntityType = "PurchaseRequest",
            Version = 1,
            IsPublished = true,
            IsActive = true,
            CreatedAtUtc = seedDate,
            PublishedAtUtc = seedDate
        });

        modelBuilder.Entity<WorkflowStepTemplate>().HasData(
            new WorkflowStepTemplate
            {
                Id = 1,
                ProcessTemplateId = 1,
                Code = "DRAFT",
                Name = "Draft",
                DisplayOrder = 1,
                IsInitial = true
            },
            new WorkflowStepTemplate
            {
                Id = 2,
                ProcessTemplateId = 1,
                Code = "DEPARTMENT_REVIEW",
                Name = "Department Review",
                DisplayOrder = 2
            },
            new WorkflowStepTemplate
            {
                Id = 3,
                ProcessTemplateId = 1,
                Code = "FINANCE_REVIEW",
                Name = "Finance Review",
                DisplayOrder = 3
            },
            new WorkflowStepTemplate
            {
                Id = 4,
                ProcessTemplateId = 1,
                Code = "APPROVED",
                Name = "Approved",
                DisplayOrder = 4,
                IsTerminal = true
            },
            new WorkflowStepTemplate
            {
                Id = 5,
                ProcessTemplateId = 1,
                Code = "REJECTED",
                Name = "Rejected",
                DisplayOrder = 5,
                IsTerminal = true
            });

        modelBuilder.Entity<WorkflowActionTemplate>().HasData(
            new WorkflowActionTemplate
            {
                Id = 1,
                FromStepTemplateId = 1,
                ToStepTemplateId = 2,
                Code = "SUBMIT",
                Name = "Submit",
                RequiresComment = false
            },
            new WorkflowActionTemplate
            {
                Id = 2,
                FromStepTemplateId = 2,
                ToStepTemplateId = 3,
                Code = "APPROVE",
                Name = "Approve department review",
                RequiresComment = false
            },
            new WorkflowActionTemplate
            {
                Id = 3,
                FromStepTemplateId = 2,
                ToStepTemplateId = 5,
                Code = "REJECT",
                Name = "Reject department review",
                RequiresComment = true
            },
            new WorkflowActionTemplate
            {
                Id = 4,
                FromStepTemplateId = 3,
                ToStepTemplateId = 4,
                Code = "APPROVE",
                Name = "Approve finance review",
                RequiresComment = false
            },
            new WorkflowActionTemplate
            {
                Id = 5,
                FromStepTemplateId = 3,
                ToStepTemplateId = 5,
                Code = "REJECT",
                Name = "Reject finance review",
                RequiresComment = true
            });

        modelBuilder.Entity<WorkflowActionerTemplate>().HasData(
            new WorkflowActionerTemplate
            {
                Id = 1,
                ActionTemplateId = 1,
                ActionerType = WorkflowActionerType.Requester
            },
            new WorkflowActionerTemplate
            {
                Id = 2,
                ActionTemplateId = 2,
                ActionerType = WorkflowActionerType.Role,
                ActionerKey = "DEPARTMENT_APPROVER"
            },
            new WorkflowActionerTemplate
            {
                Id = 3,
                ActionTemplateId = 3,
                ActionerType = WorkflowActionerType.Role,
                ActionerKey = "DEPARTMENT_APPROVER"
            },
            new WorkflowActionerTemplate
            {
                Id = 4,
                ActionTemplateId = 4,
                ActionerType = WorkflowActionerType.Role,
                ActionerKey = "FINANCE_APPROVER"
            },
            new WorkflowActionerTemplate
            {
                Id = 5,
                ActionTemplateId = 5,
                ActionerType = WorkflowActionerType.Role,
                ActionerKey = "FINANCE_APPROVER"
            });
    }
}
