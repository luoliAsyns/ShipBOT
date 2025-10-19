using LuoliCommon.DTO.Coupon;
using LuoliCommon.DTO.ExternalOrder;
using LuoliCommon.Entities;

namespace ShipBOT;

public interface IShipBOT
{
    (bool, string) Validate(CouponDTO couponDTO, ExternalOrderDTO dto);

    Task<ApiResponse<bool>> Ship(CouponDTO dto);
    Task<ApiResponse<bool>> SendMsg(CouponDTO  dto);
}