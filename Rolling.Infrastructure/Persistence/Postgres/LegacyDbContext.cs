using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Persistence.Postgres;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<PaymentTransaction> Transactions => Set<PaymentTransaction>();

    public DbSet<CourierOrder> CourierOrders => Set<CourierOrder>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OrderTimelineEvent> OrderTimelineEvents => Set<OrderTimelineEvent>();

    public DbSet<PosterUser> Users => Set<PosterUser>();

    public DbSet<AdminCredential> AdminCredentials => Set<AdminCredential>();

    public DbSet<AdminRefreshToken> AdminRefreshTokens => Set<AdminRefreshToken>();

    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    public DbSet<BusinessTime> Times => Set<BusinessTime>();

    public DbSet<Banner> Banners => Set<Banner>();

    public DbSet<ChatThreadRecord> ChatThreads => Set<ChatThreadRecord>();

    public DbSet<ChatMessageRecord> ChatMessages => Set<ChatMessageRecord>();

    public DbSet<ChatParticipantRecord> ChatParticipants => Set<ChatParticipantRecord>();

    public DbSet<BranchConfiguration> BranchConfigurations => Set<BranchConfiguration>();

    public DbSet<TakeawayAutomationSettings> TakeawayAutomationSettings => Set<TakeawayAutomationSettings>();

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
            entity.ToTable("courier_orders");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.CourierId).HasColumnName("courier_id");
            entity.Property(e => e.OrderDataJson).HasColumnName("order_data_json");
            entity.Property(e => e.ProductsJson).HasColumnName("products_json");
            entity.Property(e => e.Status).HasColumnName("status");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrderNumber).HasColumnName("order_number");
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.Subtotal).HasColumnName("subtotal");
            entity.Property(e => e.DeliveryFee).HasColumnName("delivery_fee");
            entity.Property(e => e.Discount).HasColumnName("discount");
            entity.Property(e => e.Total).HasColumnName("total");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.DeliveryAddress).HasColumnName("delivery_address");
            entity.Property(e => e.DeliveryLatitude).HasColumnName("delivery_latitude");
            entity.Property(e => e.DeliveryLongitude).HasColumnName("delivery_longitude");
            entity.Property(e => e.DeliveryAddressComment).HasColumnName("delivery_address_comment");
            entity.Property(e => e.PaymentMethod).HasColumnName("payment_method");
            entity.Property(e => e.PaymentMethodId).HasColumnName("payment_method_id");
            entity.Property(e => e.PaymentTransactionId).HasColumnName("payment_transaction_id");
            entity.Property(e => e.PaymentErrorCode).HasColumnName("payment_error_code");
            entity.Property(e => e.PaymentErrorMessage).HasColumnName("payment_error_message");
            entity.Property(e => e.PaymentAttempts).HasColumnName("payment_attempts");
            entity.Property(e => e.SavedCardToken).HasColumnName("saved_card_token");
            entity.Property(e => e.EstimatedDeliveryTime).HasColumnName("estimated_delivery_time");
            entity.Property(e => e.ActualDeliveryTime).HasColumnName("actual_delivery_time");
            entity.Property(e => e.EstimatedDeliveryMinutesMin).HasColumnName("estimated_delivery_minutes_min");
            entity.Property(e => e.EstimatedDeliveryMinutesMax).HasColumnName("estimated_delivery_minutes_max");
            entity.Property(e => e.BranchId).HasColumnName("branch_id");
            entity.Property(e => e.BranchName).HasColumnName("branch_name");
            entity.Property(e => e.BranchAddress).HasColumnName("branch_address");
            entity.Property(e => e.BranchPhone).HasColumnName("branch_phone");
            entity.Property(e => e.PosterSpotId).HasColumnName("poster_spot_id");
            entity.Property(e => e.PosterIncomingOrderId).HasColumnName("poster_incoming_order_id");
            entity.Property(e => e.PosterTransactionId).HasColumnName("poster_transaction_id");
            entity.Property(e => e.CountedTowardsLoyalty).HasColumnName("counted_towards_loyalty");
            entity.Property(e => e.LoyaltyPointsSpent).HasColumnName("loyalty_points_spent");
            entity.Property(e => e.LoyaltyPointsEarnedPending).HasColumnName("loyalty_points_earned_pending");
            entity.Property(e => e.LoyaltyPointsEarnedActual).HasColumnName("loyalty_points_earned_actual");
            entity.Property(e => e.ReceiptUrl).HasColumnName("receipt_url");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.AlternatePhone).HasColumnName("alternate_phone");
            entity.Property(e => e.FirstName).HasColumnName("first_name");
            entity.Property(e => e.LastName).HasColumnName("last_name");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.CourierId).HasColumnName("courier_id");
            entity.Property(e => e.CourierName).HasColumnName("courier_name");
            entity.Property(e => e.CourierRating).HasColumnName("courier_rating");
            entity.Property(e => e.CourierPhotoUrl).HasColumnName("courier_photo_url");
            entity.Property(e => e.CourierVehicle).HasColumnName("courier_vehicle");
            entity.Property(e => e.CourierLicensePlate).HasColumnName("courier_license_plate");
            entity.Property(e => e.CourierPhone).HasColumnName("courier_phone");
            entity.Property(e => e.CourierLatitude).HasColumnName("courier_latitude");
            entity.Property(e => e.CourierLongitude).HasColumnName("courier_longitude");
            entity.Property(e => e.CourierLocationLastUpdated).HasColumnName("courier_location_last_updated");
            entity.Property(e => e.ServiceMode).HasColumnName("service_mode");
            entity.Property(e => e.TakeawayAcceptedAt).HasColumnName("takeaway_accepted_at");
            entity.Property(e => e.TakeawayPreparingAt).HasColumnName("takeaway_preparing_at");
            entity.Property(e => e.TakeawayReadyForPickupAt).HasColumnName("takeaway_ready_for_pickup_at");
            entity.Property(e => e.PromoCode).HasColumnName("promo_code");
            entity.Property(e => e.PromoDiscountAmount).HasColumnName("promo_discount_amount");
            entity.Property(e => e.PromoDiscountPercentage).HasColumnName("promo_discount_percentage");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.UserOrderCount).HasColumnName("user_order_count");
            entity.Property(e => e.FcmToken).HasColumnName("fcm_token");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasMany(e => e.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId);
            entity.HasMany(e => e.Timeline)
                .WithOne(t => t.Order)
                .HasForeignKey(t => t.OrderId);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.MenuItemId).HasColumnName("menu_item_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.Price).HasColumnName("price");
            entity.Property(e => e.TotalPrice).HasColumnName("total_price");
            entity.Property(e => e.Modifiers).HasColumnName("modifiers");
            entity.Property(e => e.ModifierId).HasColumnName("modifier_id");
            entity.Property(e => e.ImageUrl).HasColumnName("image_url");
            entity.Property(e => e.IsBonus).HasColumnName("is_bonus");
            entity.Property(e => e.PriceOverride).HasColumnName("price_override");
        });

        modelBuilder.Entity<OrderTimelineEvent>(entity =>
        {
            entity.ToTable("order_timeline_events");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Time).HasColumnName("time");
            entity.Property(e => e.IsCompleted).HasColumnName("is_completed");
            entity.Property(e => e.IsCurrent).HasColumnName("is_current");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
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

        modelBuilder.Entity<AdminCredential>(entity =>
        {
            entity.ToTable("admin_credentials");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Login).HasColumnName("login");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.PasswordSalt).HasColumnName("password_salt");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(e => e.Login).IsUnique();
            entity.HasMany(e => e.RefreshTokens)
                .WithOne(token => token.AdminCredential)
                .HasForeignKey(token => token.AdminCredentialId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AdminRefreshToken>(entity =>
        {
            entity.ToTable("admin_refresh_tokens");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AdminCredentialId).HasColumnName("admin_credential_id");
            entity.Property(e => e.TokenHash).HasColumnName("token_hash");
            entity.Property(e => e.ReplacedByTokenHash).HasColumnName("replaced_by_token_hash");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.AdminCredentialId, e.ExpiresAt });
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
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<BusinessTime>(entity =>
        {
            entity.ToTable("times");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OpenedTime).HasColumnName("opened_time");
            entity.Property(e => e.ClosedTime).HasColumnName("closed_time");
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
            entity.Property(e => e.ImageUrls).HasColumnName("image_urls").HasDefaultValue("{}");
            entity.Property(e => e.Deeplinks).HasColumnName("deeplinks").HasDefaultValue("{}");
            entity.Property(e => e.Platforms).HasColumnName("platforms").HasDefaultValue("{}");
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

        modelBuilder.Entity<BranchConfiguration>(entity =>
        {
            entity.ToTable("branch_configurations");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SpotId).HasColumnName("spot_id");
            entity.Property(e => e.NameEn).HasColumnName("name_en");
            entity.Property(e => e.NameRu).HasColumnName("name_ru");
            entity.Property(e => e.NameUz).HasColumnName("name_uz");
            entity.Property(e => e.ShortDescriptionEn).HasColumnName("short_description_en");
            entity.Property(e => e.ShortDescriptionRu).HasColumnName("short_description_ru");
            entity.Property(e => e.ShortDescriptionUz).HasColumnName("short_description_uz");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.IsTemporarilyClosed).HasColumnName("is_temporarily_closed");
            entity.Property(e => e.ClosureMessageEn).HasColumnName("closure_message_en");
            entity.Property(e => e.ClosureMessageRu).HasColumnName("closure_message_ru");
            entity.Property(e => e.ClosureMessageUz).HasColumnName("closure_message_uz");
            entity.Property(e => e.OpenTime).HasColumnName("open_time");
            entity.Property(e => e.CloseTime).HasColumnName("close_time");
            entity.Property(e => e.Timezone).HasColumnName("timezone");
            entity.Property(e => e.Is24Hours).HasColumnName("is_24_hours");
            entity.Property(e => e.DeliveryFee).HasColumnName("delivery_fee");
            entity.Property(e => e.FreeDeliveryThreshold).HasColumnName("free_delivery_threshold");
            entity.Property(e => e.MinOrderAmount).HasColumnName("min_order_amount");
            entity.Property(e => e.MaxOrderAmount).HasColumnName("max_order_amount");
            entity.Property(e => e.EtaMinMinutes).HasColumnName("eta_min_minutes");
            entity.Property(e => e.EtaMaxMinutes).HasColumnName("eta_max_minutes");
            entity.Property(e => e.EtaDisplayTextEn).HasColumnName("eta_display_text_en");
            entity.Property(e => e.EtaDisplayTextRu).HasColumnName("eta_display_text_ru");
            entity.Property(e => e.EtaDisplayTextUz).HasColumnName("eta_display_text_uz");
            entity.Property(e => e.DeliveryRadius).HasColumnName("delivery_radius");
            entity.Property(e => e.IsDeliveryEnabled).HasColumnName("is_delivery_enabled");
            entity.Property(e => e.IsPickupEnabled).HasColumnName("is_pickup_enabled");
            entity.Property(e => e.PickupEtaMinutes).HasColumnName("pickup_eta_minutes");
            entity.Property(e => e.PickupDiscountPercentage).HasColumnName("pickup_discount_percentage");
            entity.Property(e => e.PickupDiscountTextEn).HasColumnName("pickup_discount_text_en");
            entity.Property(e => e.PickupDiscountTextRu).HasColumnName("pickup_discount_text_ru");
            entity.Property(e => e.PickupDiscountTextUz).HasColumnName("pickup_discount_text_uz");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.AlternatePhone).HasColumnName("alternate_phone");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Whatsapp).HasColumnName("whatsapp");
            entity.Property(e => e.Telegram).HasColumnName("telegram");
            entity.Property(e => e.Instagram).HasColumnName("instagram");
            entity.Property(e => e.Latitude).HasColumnName("latitude");
            entity.Property(e => e.Longitude).HasColumnName("longitude");
            entity.Property(e => e.AddressEn).HasColumnName("address_en");
            entity.Property(e => e.AddressRu).HasColumnName("address_ru");
            entity.Property(e => e.AddressUz).HasColumnName("address_uz");
            entity.Property(e => e.CityEn).HasColumnName("city_en");
            entity.Property(e => e.CityRu).HasColumnName("city_ru");
            entity.Property(e => e.CityUz).HasColumnName("city_uz");
            entity.Property(e => e.DistrictEn).HasColumnName("district_en");
            entity.Property(e => e.DistrictRu).HasColumnName("district_ru");
            entity.Property(e => e.DistrictUz).HasColumnName("district_uz");
            entity.Property(e => e.LandmarkEn).HasColumnName("landmark_en");
            entity.Property(e => e.LandmarkRu).HasColumnName("landmark_ru");
            entity.Property(e => e.LandmarkUz).HasColumnName("landmark_uz");
            entity.Property(e => e.GoogleMapsUrl).HasColumnName("google_maps_url");
            entity.Property(e => e.YandexMapsUrl).HasColumnName("yandex_maps_url");
            entity.Property(e => e.AcceptsCash).HasColumnName("accepts_cash");
            entity.Property(e => e.AcceptsCard).HasColumnName("accepts_card");
            entity.Property(e => e.AcceptsOnlinePayment).HasColumnName("accepts_online_payment");
            entity.Property(e => e.AcceptsApplePay).HasColumnName("accepts_apple_pay");
            entity.Property(e => e.AcceptsClick).HasColumnName("accepts_click");
            entity.Property(e => e.AcceptsPayme).HasColumnName("accepts_payme");
            entity.Property(e => e.AcceptsUzum).HasColumnName("accepts_uzum");
            entity.Property(e => e.CardOnDelivery).HasColumnName("card_on_delivery");
            entity.Property(e => e.IsDineInEnabled).HasColumnName("is_dine_in_enabled");
            entity.Property(e => e.IsReservationEnabled).HasColumnName("is_reservation_enabled");
            entity.Property(e => e.IsPreOrderEnabled).HasColumnName("is_pre_order_enabled");
            entity.Property(e => e.IsLoyaltyEnabled).HasColumnName("is_loyalty_enabled");
            entity.Property(e => e.IsPromoCodesEnabled).HasColumnName("is_promo_codes_enabled");
            entity.Property(e => e.IsGiftCardsEnabled).HasColumnName("is_gift_cards_enabled");
            entity.Property(e => e.IsCateringEnabled).HasColumnName("is_catering_enabled");
            entity.Property(e => e.ShowRatings).HasColumnName("show_ratings");
            entity.Property(e => e.ShowWaitTime).HasColumnName("show_wait_time");
            entity.Property(e => e.LogoUrl).HasColumnName("logo_url");
            entity.Property(e => e.BannerUrl).HasColumnName("banner_url");
            entity.Property(e => e.ThumbnailUrl).HasColumnName("thumbnail_url");
            entity.Property(e => e.GalleryUrlsJson).HasColumnName("gallery_urls_json");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.TagsJson).HasColumnName("tags_json");
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(e => e.SpotId).IsUnique();
        });

        modelBuilder.Entity<TakeawayAutomationSettings>(entity =>
        {
            entity.ToTable("takeaway_automation_settings");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PreparingDelaySeconds).HasColumnName("preparing_delay_seconds");
            entity.Property(e => e.ReadyForPickupDelaySeconds).HasColumnName("ready_for_pickup_delay_seconds");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
