using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Persistence.Postgres;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<PaymentTransaction> Transactions => Set<PaymentTransaction>();

    public DbSet<CourierOrder> Orders => Set<CourierOrder>();

    public DbSet<PosterUser> Users => Set<PosterUser>();

    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    public DbSet<BusinessTime> Times => Set<BusinessTime>();

    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();

    public DbSet<Banner> Banners => Set<Banner>();

    public DbSet<ChatThreadRecord> ChatThreads => Set<ChatThreadRecord>();

    public DbSet<ChatMessageRecord> ChatMessages => Set<ChatMessageRecord>();

    public DbSet<ChatParticipantRecord> ChatParticipants => Set<ChatParticipantRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.ToTable("transactions");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.OrderDetailsJson).HasColumnName("order_details_json");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.Amount).HasColumnName("amount");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.CreateTime).HasColumnName("create_time");
            entity.Property(e => e.PerformTime).HasColumnName("perform_time");
            entity.Property(e => e.CancelTime).HasColumnName("cancel_time");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.PrepareId).HasColumnName("prepare_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<CourierOrder>(entity =>
        {
            entity.ToTable("orders");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.CourierId).HasColumnName("courier_id");
            entity.Property(e => e.OrderDataJson).HasColumnName("order_data_json");
            entity.Property(e => e.ProductsJson).HasColumnName("products_json");
            entity.Property(e => e.Status).HasColumnName("status");
        });

        modelBuilder.Entity<PosterUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Login).HasColumnName("login");
            entity.Property(e => e.RoleName).HasColumnName("role_name");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.UserType).HasColumnName("user_type");
            entity.Property(e => e.AccessMask).HasColumnName("access_mask");
            entity.Property(e => e.LastIn).HasColumnName("last_in");
        });

        modelBuilder.Entity<NotificationRecord>(entity =>
        {
            entity.ToTable("notifications");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EnTitle).HasColumnName("en_title");
            entity.Property(e => e.EnBody).HasColumnName("en_body");
            entity.Property(e => e.RuTitle).HasColumnName("ru_title");
            entity.Property(e => e.RuBody).HasColumnName("ru_body");
            entity.Property(e => e.UzTitle).HasColumnName("uz_title");
            entity.Property(e => e.UzBody).HasColumnName("uz_body");
        });

        modelBuilder.Entity<BusinessTime>(entity =>
        {
            entity.ToTable("times");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OpenedTime).HasColumnName("opened_time");
            entity.Property(e => e.ClosedTime).HasColumnName("closed_time");
        });

        modelBuilder.Entity<NotificationEvent>(entity =>
        {
            entity.ToTable("notification_events");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Channel).HasColumnName("channel");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<Banner>(entity =>
        {
            entity.ToTable("banners");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Subtitle).HasColumnName("subtitle");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.ImageUrl).HasColumnName("image_url");
            entity.Property(e => e.Lang).HasColumnName("lang");
            entity.Property(e => e.Path).HasColumnName("path");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<ChatThreadRecord>(entity =>
        {
            entity.ToTable("chat_threads");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasMany(e => e.Participants)
                .WithOne()
                .HasForeignKey(participant => participant.ThreadId);
            entity.HasIndex(e => new { e.TenantId, e.OrderId, e.CustomerId }).IsUnique();
        });

        modelBuilder.Entity<ChatParticipantRecord>(entity =>
        {
            entity.ToTable("chat_participants");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ThreadId).HasColumnName("thread_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.HasIndex(e => new { e.ThreadId, e.UserId }).IsUnique();
        });

        modelBuilder.Entity<ChatMessageRecord>(entity =>
        {
            entity.ToTable("chat_messages");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ThreadId).HasColumnName("thread_id");
            entity.Property(e => e.SenderId).HasColumnName("sender_id");
            entity.Property(e => e.SenderRole).HasColumnName("sender_role");
            entity.Property(e => e.ContentType).HasColumnName("content_type");
            entity.Property(e => e.Body).HasColumnName("body");
            entity.Property(e => e.SentAt).HasColumnName("sent_at");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.HasIndex(e => new { e.ThreadId, e.SentAt });
        });
    }
}
