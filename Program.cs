using System.Reflection;
using System.Text;
using System.Text.Json;
using LuoliCommon;
using LuoliCommon.DTO.Coupon;
using LuoliCommon.DTO.ExternalOrder;
using LuoliCommon.Enums;
using LuoliCommon.Logger;
using LuoliHelper.Utils;
using LuoliUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ShipBOT;

class Program
{
    private static ILogger _logger;
    public static Config Config;

    public static RabbitMQConnection RabbitMQConnection;
    public static RedisConnection RedisConnection;
    public static IShipBOT Bot;


    public static List<string> NotifyUsers;


    private static bool init()
    {
        bool result = false;
        string configFolder = "/app/ShipBOT/configs";

#if DEBUG
        configFolder = "debugConfigs";
#endif

        ActionsOperator.TryCatchAction(() =>
        {
            Config = new Config($"{configFolder}/sys.json");

            NotifyUsers = Config.KVPairs["NotifyUsers"].Split(',').Select(s => s.Trim())
                .Where(s => !String.IsNullOrEmpty(s)).ToList();

            RedisConnection = new RedisConnection($"{configFolder}/redis.json");

            var rds = new CSRedis.CSRedisClient(
                $"{RedisConnection.Host}:{RedisConnection.Port},password={RedisConnection.Password},defaultDatabase={RedisConnection.DatabaseId}");
            RedisHelper.Initialization(rds);

            RabbitMQConnection = new RabbitMQConnection($"{configFolder}/rabbitmq.json");

            result = true;
        });

        return result;
    }

    public static async Task Main(string[] args)
    {

        Environment.CurrentDirectory = AppContext.BaseDirectory;

        if (!init())
            throw new Exception("initial failed; cannot start");

        #region add ILogger

        var services = new ServiceCollection();
        services.AddHttpClient("LokiHttpClient")
            .ConfigureHttpClient(client =>
            {
                // client.DefaultRequestHeaders.Add("X-Custom-Header", "luoli-app");
            });
        
        // 添加 luoli的 ILogger   loki logger
        services.AddSingleton<LuoliCommon.Logger.ILogger, LokiLogger>(provider =>
        {
            var httpClient = provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("LokiHttpClient");

            var dict = new Dictionary<string, string>();
            dict["app"]= Config.ServiceName;

            var loki = new LokiLogger(Config.KVPairs["LokiEndPoint"],
                dict,
                httpClient);
            loki.AfterLog = (msg) => Console.WriteLine(msg);
            return loki;
        });
        
        #endregion

        #region  add rabbitmq

        services.AddSingleton<RabbitMQ.Client.IConnectionFactory>(provider =>
        {
            return new ConnectionFactory
            {
                HostName = RabbitMQConnection.Host,
                Port = RabbitMQConnection.Port,
                UserName = RabbitMQConnection.UserId,
                Password = RabbitMQConnection.UserId,
                VirtualHost = "/",
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };
        });

        services.AddSingleton<IConnection>(provider =>
        {
            var factory = provider.GetRequiredService<RabbitMQ.Client.IConnectionFactory>();
            return factory.CreateConnectionAsync().Result;
        });

        services.AddSingleton<IChannel>(provider =>
        {
            var connection = provider.GetRequiredService<IConnection>();
            return connection.CreateChannelAsync().Result;
        });

        #endregion

        //消费消息
        services.AddHostedService<ConsumerService>();

        ServiceLocator.Initialize( services.BuildServiceProvider());

        _logger =  ServiceLocator.GetService<LuoliCommon.Logger.ILogger>();

        #region luoli code

        // 应用启动后，通过服务容器获取 LokiLogger 实例
        var prov = services.BuildServiceProvider();
        try
        {
            // 获取 LokiLogger 实例
            var lokiLogger = prov.GetRequiredService<LuoliCommon.Logger.ILogger>();

            // 记录启动日志
            lokiLogger.Info("应用程序启动成功");
            lokiLogger.Debug($"端口：{Config.BindAddr}");


            var assembly = Assembly.GetExecutingAssembly();
            var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            var fileVersion = fileVersionInfo.FileVersion;

            lokiLogger.Info($"CurrentDirectory:[{Environment.CurrentDirectory}]");
            lokiLogger.Info($"Current File Version:[{fileVersion}]");
        }
        catch (Exception ex)
        {
            // 启动日志失败时降级输出
            Console.WriteLine($"启动日志记录失败：{ex.Message}");
        }


        #endregion

       
        
        await ApiCaller.NotifyAsync($"{Config.ServiceName}.{Config.ServiceId} 启动了", NotifyUsers);

        int count = 0;
        int successCount = 0;

        // if (Config.KVPairs["BOTType"] == "Sexytea")
        //     Bot = new SexyteaPlaceOrderBOT();
        // else
        //     throw new Exception($"unknown BOTType:{Config.KVPairs["BOTType"]}");

        
        
        Console.ReadLine();
    }


    private static void Notify(CouponDTO coupon, ExternalOrderDTO externalOrder, string coreMsg)
    {
        ApiCaller.NotifyAsync(
            @$"{Config.ServiceName}.{Config.ServiceId}
{coreMsg}

卡密:{coupon.Coupon}
卡密状态:{EnumOperator.GetDescription((ECouponStatus)coupon.Status)}
卡密金额:{coupon.AvailableBalance}
卡密绑定订单:{coupon.ExternalOrderFromPlatform} - {coupon.ExternalOrderTid}
订单金额:{externalOrder.PayAmount}
订单内容:{JsonSerializer.Serialize(externalOrder.Order)}", NotifyUsers);
    }
}