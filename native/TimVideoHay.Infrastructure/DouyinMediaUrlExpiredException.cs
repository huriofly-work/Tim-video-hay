using TimVideoHay.Core;
namespace TimVideoHay.Infrastructure;

public sealed class DouyinMediaUrlExpiredException() : NeedsActionException("Link tải đã hết hạn hoặc bị CDN từ chối; cần làm mới đúng bản video.");
