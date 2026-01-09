namespace Rolling.Infrastructure.Configuration;

public sealed class PosterCacheRefreshOptions
{
    public const string SectionName = "PosterCacheRefresh";

    public int ProductsSeconds { get; set; } = 3600;
    public int CategoriesSeconds { get; set; } = 7200;
    public int PromotionsSeconds { get; set; } = 7200;
    public int ClientGroupsSeconds { get; set; } = 21600;
    public int SpotsSeconds { get; set; } = 7200;
    public int EmployeesSeconds { get; set; } = 21600;
    public bool WarmOnStartup { get; set; } = true;
}
