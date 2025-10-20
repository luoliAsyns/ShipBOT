using LuoliCommon.DTO.Coupon;
using LuoliCommon.DTO.ExternalOrder;
using LuoliCommon.Entities;
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

        public async Task<ApiResponse<bool>> SendMsg(CouponDTO coupon)
        {

            string msg = await RedisHelper.GetAsync<string>($"msg.template");

            string rawLink = $"{Program.Config.KVPairs["ConsumeUrl"]}?coupon={coupon.Coupon}";
            string shortlink = $"{Program.Config.KVPairs["ConsumeUrl"]}?sl={LuoliUtils.Decoder.GenerateShortCode(rawLink)}";

            msg = msg.Replace("{tid}", coupon.ExternalOrderTid);
            msg = msg.Replace("{link}", shortlink);

            RedisHelper.SetAsync(shortlink, rawLink, 24*60*60);

            coupon.RawUrl = rawLink;
            coupon.ShortUrl = shortlink;

            var resp = await _agisoApis.SendWWMsg(
                Program.Config.KVPairs["AgisoAccessToken"],
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

        public async Task<ApiResponse<bool>> Ship(CouponDTO coupon)
        {

            var resp = await _agisoApis.ShipOrder(
                Program.Config.KVPairs["AgisoAccessToken"],
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

            return (true , string.Empty);
        }
    }
}
