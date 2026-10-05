using AUnrealFeatures.Hosting;
using AUnrealFeatures.Hosting.Application;
using AUnrealFeatures.Hosting.Database;
using AUnrealFeatures.Hosting.Database.Interfaces;
using AUnrealFeatures.Hosting.Configuration;
using AUnrealFeatures.Ares.Models;
using AUnrealFeatures.Ares.Servers;
using AUnrealFeatures.EOSSDK;
using AUnrealFeatures.AAMothership;
using AUnrealFeatures.AAMothership.Models;
using AUnrealFeatures.Playfab;
using AUnrealFeatures.Playfab.Models;
using AUnrealFeatures.Virtex;
using AUnrealFeatures.GameSocket;
using AUnrealFeatures.ContentDownloadNetwork;

namespace AUnrealFeatures.Ares
{
    public static class Program
    {
        private static IDatabase _database = null!;
        private static IHostApplication _hostApplication = null!;

        public static IDatabase Database => _database;

        public static async Task Main(string[] args)
        {
            ModuleInitialization.Initialize();
            var productionDatabasePassword = EnvironmentSettings.Get("AUNREAL_DATABASE_PASSWORD");

            _hostApplication = HostApplication.New("AUnrealFeatures")
                .ConfigureServices(services =>
                {
                    var databaseManager = AstraDatabaseBuilder.Shared
                        .WithDatabase("production", new DatabaseOptions
                        {
                            databaseId = 1,
                            friendlyName = "Production Database",
                            usePassword = !string.IsNullOrEmpty(productionDatabasePassword),
                            password = productionDatabasePassword
                        })
                        .WithDatabase("development", new DatabaseOptions
                        {
                            databaseId = 2,
                            friendlyName = "Development Database",
                            usePassword = false,
                            password = string.Empty
                        })
                        .Build();


                    if (databaseManager != null)
                    {
                        services.AddSingleton<AstraDatabaseManager, IDatabaseManager>(
                            (AstraDatabaseManager?)databaseManager
                        );

#if DEBUG
                        var database = (AstraDatabase?)databaseManager.GetDatabase(name: "development", id: 2);
#elif !DEBUG
                        var database = (AstraDatabase?)databaseManager.GetDatabase(name: "production", id: 1);
#endif

                        _database = database;
                        services.AddSingleton<AstraDatabase, IDatabase>(database);
                        database?.GetCollection<UserDataResponse>(true);
                        database?.GetCollection<RoleResponse>(true);
                        database?.GetCollection<StationDbObject>(true);
                        database?.GetCollection<DeploymentDbObject>(true);
                        database?.GetCollection<ServerEventDbObject>(true);
                        database?.GetCollection<StationEventDbObject>(true);
                        database?.GetCollection<EventSignupDbObject>(true);
                        database?.GetCollection<UserApiKeyDbObject>(true);
                        MothershipServer.Database = database!;
                        database?.GetCollection<MothershipPlayerDbObject>(true);
                        database?.GetCollection<MothershipSessionDbObject>(true);
                        database?.GetCollection<MothershipV2PlayerDbObject>(true);
                        database?.GetCollection<MothershipAssociationDbObject>(true);
                        database?.GetCollection<MothershipLinkDbObject>(true);
                        database?.GetCollection<MothershipSharedGroupDbObject>(true);
                        database?.GetCollection<MothershipReportDbObject>(true);
                        database?.GetCollection<MothershipBanDbObject>(true);
                        database?.GetCollection<MothershipMuteDbObject>(true);

                        // PlayFab-compatible service storage.
                        PlayfabServer.Database = database!;
                        database?.GetCollection<PlayfabPlayerDbObject>(true);
                        database?.GetCollection<PlayfabSessionDbObject>(true);
                        database?.GetCollection<PlayfabUserDataDbObject>(true);
                        database?.GetCollection<PlayfabTitleDataDbObject>(true);
                        database?.GetCollection<PlayfabSharedGroupDbObject>(true);
                        database?.GetCollection<PlayfabFriendDbObject>(true);
                        database?.GetCollection<PlayfabStatisticDbObject>(true);
                        database?.GetCollection<PlayfabStatisticDefinitionDbObject>(true);
                        database?.GetCollection<PlayfabCharacterDbObject>(true);
                        database?.GetCollection<PlayfabCharacterDataDbObject>(true);
                        database?.GetCollection<PlayfabSegmentDbObject>(true);
                        database?.GetCollection<PlayfabPlayerTagDbObject>(true);
                        database?.GetCollection<PlayfabPushTokenDbObject>(true);
                        database?.GetCollection<PlayfabTradeDbObject>(true);
                        database?.GetCollection<PlayfabPurchaseDbObject>(true);
                        database?.GetCollection<PlayfabContentDbObject>(true);
                        database?.GetCollection<PlayfabInventoryItemDbObject>(true);
                        database?.GetCollection<PlayfabVirtualCurrencyDbObject>(true);
                        database?.GetCollection<PlayfabCatalogItemDbObject>(true);
                        database?.GetCollection<PlayfabStoreItemDbObject>(true);
                        database?.GetCollection<PlayfabGroupDbObject>(true);
                        database?.GetCollection<PlayfabGroupRoleDbObject>(true);
                        database?.GetCollection<PlayfabGroupMembershipDbObject>(true);
                        database?.GetCollection<PlayfabGroupInvitationDbObject>(true);
                        database?.GetCollection<PlayfabGroupApplicationDbObject>(true);
                        database?.GetCollection<PlayfabGroupBlockDbObject>(true);
                        database?.GetCollection<PlayfabEconomyItemDbObject>(true);
                        database?.GetCollection<PlayfabEconomyInventoryDbObject>(true);
                        database?.GetCollection<PlayfabTransactionDbObject>(true);
                        database?.GetCollection<PlayfabReviewDbObject>(true);
                        database?.GetCollection<PlayfabFileDbObject>(true);
                        database?.GetCollection<PlayfabObjectDbObject>(true);
                        database?.GetCollection<PlayfabProfileDbObject>(true);
                        database?.GetCollection<PlayfabLobbyDbObject>(true);
                        database?.GetCollection<PlayfabMatchmakingTicketDbObject>(true);
                        database?.GetCollection<PlayfabMatchmakingQueueDbObject>(true);
                        database?.GetCollection<PlayfabMatchDbObject>(true);
                        database?.GetCollection<PlayfabBuildDbObject>(true);
                        database?.GetCollection<PlayfabServerDbObject>(true);
                        database?.GetCollection<PlayfabPartyDbObject>(true);
                        database?.GetCollection<PlayfabEventDbObject>(true);
                        database?.GetCollection<PlayfabExperimentDbObject>(true);
                        database?.GetCollection<PlayfabExperimentTreatmentDbObject>(true);
                        database?.GetCollection<PlayfabLocaleDbObject>(true);
                        database?.GetCollection<PlayfabProgressionDbObject>(true);
                        database?.GetCollection<PlayfabLeaderboardEntryDbObject>(true);
                        database?.GetCollection<PlayfabAddonConfigDbObject>(true);
                        database?.GetCollection<PlayfabCloudScriptFileDbObject>(true);
                        database?.GetCollection<PlayfabCloudScriptFunctionDbObject>(true);
                        database?.GetCollection<PlayfabCloudScriptExecutionLogDbObject>(true);
                        database?.GetCollection<PlayfabContactEmailDbObject>(true);
                        database?.GetCollection<PlayfabAdPlacementDbObject>(true);
                        database?.GetCollection<PlayfabPolicyDbObject>(true);
                        // Helper-table types declared in non-Models files
                        database?.GetCollection<PlayfabInsightsConfigDbObject>(true);
                        database?.GetCollection<PlayfabInsightsOperationDbObject>(true);
                        database?.GetCollection<PlayfabTelemetryKeyDbObject>(true);
                        database?.GetCollection<PlayfabDataConnectionDbObject>(true);
                        database?.GetCollection<PlayfabExclusionGroupDbObject>(true);
                        database?.GetCollection<PlayfabStatisticDefinitionV2DbObject>(true);
                        database?.GetCollection<PlayfabEntityStatisticDbObject>(true);
                        database?.GetCollection<PlayfabLeaderboardDefinitionDbObject>(true);
                        database?.GetCollection<PlayfabCatalogConfigDbObject>(true);
                        database?.GetCollection<PlayfabPendingPurchaseDbObject>(true);
                        database?.GetCollection<PlayfabBuildAliasDbObject>(true);
                        database?.GetCollection<PlayfabAssetDbObject>(true);
                        database?.GetCollection<PlayfabCertificateDbObject>(true);
                        database?.GetCollection<PlayfabSecretDbObject>(true);
                        database?.GetCollection<PlayfabContainerImageDbObject>(true);
                        database?.GetCollection<PlayfabRemoteUserDbObject>(true);
                        database?.GetCollection<PlayfabTitleMpsConfigDbObject>(true);
                        database?.GetCollection<PlayfabQuotaChangeDbObject>(true);
                        database?.GetCollection<PlayfabPhotonRoomStateDbObject>(true);
                        database?.GetCollection<PlayfabPhotonWebhookLogDbObject>(true);
                        database?.GetCollection<PlayfabPhotonTicketDbObject>(true);
                        database?.GetCollection<PlayfabGenericIdDbObject>(true);
                        database?.GetCollection<PlayfabPlayerCustomPropertyDbObject>(true);
                        database?.GetCollection<PlayfabVirtualCurrencyDefDbObject>(true);
                        database?.GetCollection<PlayfabTitleNewsDbObject>(true);
                    }
                })
                .AddServer<IStationDbServer, StationDbServer>()
                .AddServer<IEosGatewayServer, EosGatewayServer>()
                .AddServer<IEosWsServer, EosWsServer>()
                .AddServer<IAresDashboardServer, AresDashboardServer>()
                .AddServer<IMothershipServer, MothershipServer>()
                .AddServer<IPlayfabServer, PlayfabServer>()
                .AddServer<IVirtexServer, VirtexServer>()
                .AddServer<ISkyboxServer, SkyboxServer>()
                .AddServer<IGameSocketServer, GameSocketServer>()
                .AddServer<IContentDownloadNetworkServer, ContentDownloadNetworkServer>();

            await _hostApplication.RunAsync();
        }
    }
}
