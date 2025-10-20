using LuoliCommon.DTO.Coupon;
using LuoliCommon.DTO.ExternalOrder;
using LuoliUtils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.ServiceModel.Channels;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ThirdApis;
using IChannel = RabbitMQ.Client.IChannel;

namespace ShipBOT
{
    public class ConsumerService : BackgroundService
    {
        private readonly IChannel _channel;
        private readonly AsynsApis _asynsApis;
        private readonly string _queueName =RabbitMQKeys.CouponGenerated; // 替换为你的队列名
        private readonly LuoliCommon.Logger.ILogger _logger;

        private readonly IShipBOT Bot;

        private static JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, // 关键配置：忽略大小写
        };


        public ConsumerService(IChannel channel,
             AsynsApis asynsApis,
             LuoliCommon.Logger.ILogger logger,
             IShipBOT bot
             )
        {
            _channel = channel;
            _logger = logger;
            _asynsApis = asynsApis;
            Bot = bot;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 声明队列
            await _channel.QueueDeclareAsync(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: stoppingToken);

            // 设置Qos
            await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: 10,
                global: false,
                stoppingToken);

            // 创建消费者
            var consumer = new AsyncEventingBasicConsumer(_channel);

            // 处理接收到的消息
            consumer.ReceivedAsync += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                try
                {
                    _logger.Info($"收到:{message}, 开始处理");

                    var couponDto = JsonSerializer.Deserialize<CouponDTO>(message, _options);

                    //这里要重新从数据库获取订单，防止数据不一致
                    //可能收到退款通知什么的
                    var eoResp = await _asynsApis.ExternalOrderQuery(couponDto.ExternalOrderFromPlatform, couponDto.ExternalOrderTid);
                    var eoDto = eoResp.data;
                    
                    var couponResp = await _asynsApis.CouponQuery(eoDto.FromPlatform, eoDto.Tid);
                    if (!eoResp.ok && !couponResp.ok)
                    {
                        _logger.Error("订单/卡密查询失败");
                        Notify(couponDto, eoDto, "订单/卡密查询失败", ea.DeliveryTag , stoppingToken);
                        return;
                    }
                    couponDto = couponResp.data;

                    var (validateResult, validateMsg) = Bot.Validate(couponDto, eoDto);

                    if (!validateResult)
                    {
                        _logger.Error($"订单校验失败:{validateMsg}");
                        Notify(couponDto, eoDto, $"订单校验失败:{validateMsg}", ea.DeliveryTag, stoppingToken);
                        return;
                    }

                    var shipResp = await Bot.Ship(couponDto);
                    if (!shipResp.ok)
                    {
                        _logger.Error($"发货失败:{shipResp.msg},订单 订单号:{eoDto.Tid}, 已付金额:{eoDto.PayAmount}");
                        Notify(couponDto, eoDto, $"下单失败:{shipResp.msg}", ea.DeliveryTag, stoppingToken);
                        return;
                    }


                    var sendMsgResp = await Bot.SendMsg(couponDto);
                    if (!sendMsgResp.ok)
                    {
                        _logger.Error($"发送消息失败:{sendMsgResp.msg},订单 订单号:{eoDto.Tid}, 已付金额:{eoDto.PayAmount}");
                        Notify(couponDto, eoDto, $"发送消息失败:{sendMsgResp.msg}", ea.DeliveryTag, stoppingToken);
                        return;
                    }


                    _logger.Info($"{Program.Config.ServiceName}订单处理成功 订单号:{eoDto.Tid}, 已付金额:{eoDto.PayAmount}");

                    //通知页面刷新
                    RedisHelper.Publish(RedisKeys.Pub_RefreshShipStatus, eoDto.Tid);

                    // 处理成功，确认消息
                    await _channel.BasicAckAsync(
                            deliveryTag: ea.DeliveryTag,
                            multiple: false,
                            stoppingToken);

                    await _asynsApis.CouponUpdate(new LuoliCommon.DTO.Coupon.UpdateRequest()
                    {
                        Coupon = couponDto,
                        Event = LuoliCommon.Enums.EEvent.Coupon_Shipment
                    });

                }
                catch (Exception ex)
                {
                    _logger.Error("while ConsumerService consuming");
                    _logger.Error(ex.Message);
                    // 处理异常，记录日志
                    // 异常情况下不确认消息，不重新入队
                    await _channel.BasicNackAsync(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false,
                        requeue: false,
                        stoppingToken);
                }
            };

            // 开始消费
            await _channel.BasicConsumeAsync(
                queue: _queueName,
                autoAck: false,
                consumerTag: Program.Config.ServiceName,
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer,
                stoppingToken);

            // 保持服务运行直到应用程序停止
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }


        /// <summary>
        /// ShipFailed  统一处理
        /// </summary>
        /// <param name="coupon"></param>
        /// <param name="externalOrder"></param>
        /// <param name="coreMsg"></param>
        private void Notify(CouponDTO coupon, ExternalOrderDTO externalOrder, string coreMsg, ulong tag, CancellationToken token)
        {
            RedisHelper.Publish(RedisKeys.Pub_RefreshShipStatus, externalOrder.Tid);

            _asynsApis.CouponUpdate(new LuoliCommon.DTO.Coupon.UpdateRequest()
            {
                Coupon = coupon,
                Event = LuoliCommon.Enums.EEvent.Coupon_ShipFailed
            });

            _channel.BasicNackAsync(
                      deliveryTag: tag,
                      multiple: false,
                      requeue: false,
                      token);


            Program.Notify(
                coupon,
                externalOrder,
                coreMsg);
        }

    }



}
