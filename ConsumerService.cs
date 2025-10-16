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
        private readonly IServiceProvider _serviceProvider;
        private readonly string _queueName =RabbitMQKeys.CouponGenerated; // 替换为你的队列名
        private readonly LuoliCommon.Logger.ILogger _logger;
        public ConsumerService(IChannel channel,
             IServiceProvider serviceProvider,
             LuoliCommon.Logger.ILogger logger
             )
        {
            _channel = channel;
            _logger = logger;
            _serviceProvider = serviceProvider;
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
                    _logger.Info($"收到:{msg}, 开始处理");
                    count++;


                    MOrder order = SqlClient.Queryable<MOrder>().Where(O => O.order_no == msg).First();

                    var (validateResult, validateMsg) = Bot.Validate(order).Result;
                    if (!validateResult)
                    {
                        _logger.Error($"订单校验失败:{validateMsg}");
                        Notify(order, $"订单校验失败:{validateMsg}");
                        //通知页面刷新
                        RedisHelper.Publish(RedisKeys.CouponChanged, order.consume_coupon);
                        return;
                    }

                    var (placeResult, placeMsg) = Bot.PlaceOrder(order).Result;
                    if (!placeResult)
                    {
                        _logger.Error($"下单失败:{placeMsg},订单 订单号:{order.order_no}, 已付金额:{order.pay_amount}");
                        Notify(order, $"下单失败:{placeMsg}");
                        //通知页面刷新
                        RedisHelper.Publish(RedisKeys.CouponChanged, order.consume_coupon);
                        return;
                    }

                    var (updateResult, updateMsg) = Bot.UpdateResult(order).Result;
                    if (!updateResult)
                    {
                        _logger.Error($"更新订单状态失败:{updateMsg},订单 订单号:{order.order_no}, 已付金额:{order.pay_amount}");
                        Notify(order, $"更新订单状态失败:{updateMsg}");
                        //通知页面刷新
                        RedisHelper.Publish(RedisKeys.CouponChanged, order.consume_coupon);
                        return;
                    }



                    // 调用Controller中的方法
                    var result = await cs.GenerateAsync(dto);

                        // 如果需要处理返回结果
                        if (result.ok)
                        {

                        _logger.Info($"订单完成 订单号:{order.order_no}, 已付金额:{order.pay_amount}");
                       
                        //通知页面刷新
                        RedisHelper.Publish(RedisKeys.CouponChanged, order.consume_coupon);

                        // 处理成功，确认消息
                        await _channel.BasicAckAsync(
                                deliveryTag: ea.DeliveryTag,
                                multiple: false,
                                stoppingToken);
                        }
                        else
                        {
                            // 处理失败，不重新入队
                            _logger.Error("while ConsumerService, insert ExternalOrderDTO failed");
                            await _channel.BasicNackAsync(
                                deliveryTag: ea.DeliveryTag,
                                multiple: false,
                                requeue: false,
                                stoppingToken);
                        }
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
    }



}
