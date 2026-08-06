using IMS.Application.Common.Services;
using IMS.Application.Features.Auth;
using IMS.Application.Features.Inbound;
using IMS.Application.Features.Inventory;
using IMS.Application.Features.MasterData;
using IMS.Application.Features.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace IMS.Application;

/// <summary>Registers the application-layer use-case services.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ScopeGuard>();

        // Security
        services.AddScoped<AuthService>();

        // Faz 1 - master data
        services.AddScoped<WarehouseService>();
        services.AddScoped<ZoneService>();
        services.AddScoped<LocationService>();
        services.AddScoped<LocationProfileService>();
        services.AddScoped<ItemService>();
        services.AddScoped<ReferenceDataService>();

        // Faz 2 - inventory core. InventoryLedger is the single gateway through which
        // stock may change, so rule §11.9 cannot be bypassed by another service.
        services.AddScoped<InventoryLedger>();
        services.AddScoped<InventoryService>();
        services.AddScoped<TrackingService>();

        // Faz 3 - inbound
        services.AddScoped<InboundOrderService>();
        services.AddScoped<ReceivingService>();

        // Faz 4 - outbound
        services.AddScoped<OrderService>();
        services.AddScoped<AllocationService>();
        services.AddScoped<PickingService>();
        services.AddScoped<ShippingService>();

        return services;
    }
}
