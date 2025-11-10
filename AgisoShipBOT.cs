using LuoliCommon.DTO.Coupon;
using LuoliCommon.DTO.ExternalOrder;
using LuoliCommon.Entities;
using LuoliUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThirdApis;

namespace ShipBOT
{
    public class AgisoShipBOT : IShipBOT
    {
        private readonly AgisoApis _agisoApis;
        private readonly LuoliCommon.Logger.ILogger _logger;
        public AgisoShipBOT(AgisoApis agisoApis,
              LuoliCommon.Logger.ILogger logger)
        {
            _agisoApis = agisoApis;
            _logger = logger;
        }

        public async Task<ApiResponse<bool>> SendMsg(CouponDTO coupon, ExternalOrderDTO dto)
        {

            string msg = await RedisHelper.GetAsync<string>($"msg.template");

            string rawLink = $"{Program.Config.KVPairs["ConsumeUrl"]}?coupon={coupon.Coupon}";

            msg = msg.Replace("{tid}", coupon.ExternalOrderTid);
            msg = msg.Replace("{link}", rawLink);

            RedisHelper.SetAsync(coupon.Coupon, rawLink, 24*60*60);

            coupon.RawUrl = rawLink;
            coupon.ShortUrl = coupon.Coupon;

            var accessToken = await RedisHelper.HGetAsync(RedisKeys.AgisoAccessToken, dto.SellerNick);

            if (accessToken is null)
            {
                string notFound = $"店铺id[{dto.SellerNick}]没有找到对应的agiso access token";
                _logger.Error(notFound);

                var result = new ApiResponse<bool>();
                result.data = false;
                result.code = LuoliCommon.Enums.EResponseCode.Fail;
                result.msg = notFound;
                _logger.Error($"SendMsg failed,tid:[{coupon.ExternalOrderTid}] msg:{result.msg}, {notFound}");
                return result;
            }

            var resp = await _agisoApis.SendWWMsg(
                accessToken,
                Program.Config.KVPairs["AgisoAppSecret"],
                coupon.ExternalOrderTid,
                msg);

            if (resp.Item1)
            {
                return new ApiResponse<bool>() { code = LuoliCommon.Enums.EResponseCode.Success, data = true };
            }
            else
            {
                var result = new ApiResponse<bool>();
                result.data = false;
                result.code = LuoliCommon.Enums.EResponseCode.Fail;
                result.msg = resp.Item2;
                _logger.Error($"SendMsg failed,tid:[{coupon.ExternalOrderTid}] msg:{result.msg}");
                return result;
            }
        }

        public async Task<ApiResponse<bool>> Ship(CouponDTO coupon, ExternalOrderDTO dto)
        {

            var accessToken = await RedisHelper.HGetAsync(RedisKeys.AgisoAccessToken, dto.SellerNick);

            if (accessToken is null)
            {
                string notFound = $"店铺id[{dto.SellerNick}]没有找到对应的agiso access token";
                _logger.Error(notFound);

                var result = new ApiResponse<bool>();
                result.data = false;
                result.code = LuoliCommon.Enums.EResponseCode.Fail;
                result.msg = notFound;
                _logger.Error($"SendMsg failed,tid:[{coupon.ExternalOrderTid}] msg:{result.msg}, {notFound}");
                return result;
            }

            var resp = await _agisoApis.ShipOrder(
                accessToken,
                Program.Config.KVPairs["AgisoAppSecret"],
                coupon.ExternalOrderTid);


            if (resp.Item1)
            {
                return new ApiResponse<bool>() { code = LuoliCommon.Enums.EResponseCode.Success, data = true };
            }
            else
            {
                var result = new ApiResponse<bool>();
                result.data = false;
                result.code = LuoliCommon.Enums.EResponseCode.Fail;
                result.msg = resp.Item2;
                _logger.Error($"Ship failed,tid:[{coupon.ExternalOrderTid}] msg:{result.msg}");
                return result;
            }

        }

        public (bool, string) Validate(CouponDTO coupon, ExternalOrderDTO eo)
        {
            if(coupon.Status != LuoliCommon.Enums.ECouponStatus.Generated)
                return (false, $"CouponDTO Status:[{coupon.Status.ToString()}], must be [ECouponStatus.Generated]");

            if (coupon.CreditLimit != coupon.AvailableBalance)
                return (false, $"CouponDTO CreditLimit[{coupon.CreditLimit}] must be equal to AvailableBalance[{coupon.AvailableBalance}]");

            if(eo.Status == LuoliCommon.Enums.EExternalOrderStatus.Refunding)
                return (false, $"ExternalOrderDTO Status[{eo.Status.ToString()}], so do not process");


            return (true , string.Empty);
        }
    }
}
