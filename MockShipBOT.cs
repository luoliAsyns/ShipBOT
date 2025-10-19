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
    public class MockShipBOT : IShipBOT
    {
        private readonly AgisoApis _agisoApis;
        private readonly LuoliCommon.Logger.ILogger _logger;
        public MockShipBOT(AgisoApis agisoApis,
              LuoliCommon.Logger.ILogger logger)
        {
            _agisoApis = agisoApis;
            _logger = logger;
        }

        public async Task<ApiResponse<bool>> SendMsg(CouponDTO coupon)
        {

            string msg = await RedisHelper.GetAsync<string>(RedisKeys.WWMsgTemplate);

            string rawLink = $"{Program.Config.KVPairs["ConsumeUrl"]}?coupon={coupon.Coupon}";
            string shortlink = $"{Program.Config.KVPairs["ConsumeUrl"]}?sl={LuoliUtils.Decoder.GenerateShortCode(rawLink)}";

            RedisHelper.SetAsync(shortlink, rawLink, 24*60*60);

            return new ApiResponse<bool>() { code = LuoliCommon.Enums.EResponseCode.Success, data = true };


        }

        public async Task<ApiResponse<bool>> Ship(CouponDTO coupon)
        {
            return new ApiResponse<bool>() { code = LuoliCommon.Enums.EResponseCode.Success, data = true };
        }

        public (bool, string) Validate(CouponDTO coupon, ExternalOrderDTO eo)
        {

            return (true , string.Empty);
        }
    }
}
