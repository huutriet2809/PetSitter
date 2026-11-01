const fs = require('fs');
const path = require('path');

const analyzedPath = path.resolve(__dirname, 'analyzed_calls.json');
const analyzed = fs.existsSync(analyzedPath) ? JSON.parse(fs.readFileSync(analyzedPath, 'utf8')) : [];

// Backend endpoints map
const backendEndpoints = [
  { method: 'POST', path: '/api/auth/login', desc: 'Đăng nhập', dtoIn: 'LoginDto { email, password }', dtoOut: 'UserDto { userId, fullName, email, role, token, phone, avatarUrl }' },
  { method: 'POST', path: '/api/auth/register', desc: 'Đăng ký', dtoIn: 'RegisterDto { email, password, fullName, phone, role }', dtoOut: 'UserDto' },
  { method: 'GET', path: '/api/auth/me', desc: 'Lấy user hiện tại', dtoIn: 'None', dtoOut: 'UserDto { userId, fullName, email, role, phone, avatarUrl }' },
  { method: 'PUT', path: '/api/auth/me', desc: 'Cập nhật user', dtoIn: 'UpdateProfileDto { name, phone, avatarUrl }', dtoOut: 'UserDto' },
  { method: 'POST', path: '/api/auth/change-password', desc: 'Đổi mật khẩu', dtoIn: 'ChangePasswordDto { currentPassword, newPassword }', dtoOut: '{ message }' },
  { method: 'POST', path: '/api/auth/forgot-password', desc: 'Quên mật khẩu', dtoIn: 'ForgotPasswordDto { email }', dtoOut: '{ message, [devResetLink] }' },
  { method: 'POST', path: '/api/auth/reset-password', desc: 'Đặt lại mật khẩu', dtoIn: 'ResetPasswordDto { token, newPassword }', dtoOut: '{ message }' },

  { method: 'POST', path: '/api/bookings', desc: 'Tạo booking', dtoIn: 'CreateBookingDto { sitterId, ngayBatDau, ngayKetThuc, tenPet, loaiPet, kichCo, petId, ghiChu, addons }', dtoOut: 'BookingDetailDto { bookingId, ... }' },
  { method: 'GET', path: '/api/bookings/my', desc: 'Lấy bookings của user', dtoIn: 'None', dtoOut: 'List<BookingDetailDto>' },
  { method: 'GET', path: '/api/bookings/{id}', desc: 'Chi tiết booking', dtoIn: 'None', dtoOut: 'BookingDetailDto' },
  { method: 'POST', path: '/api/bookings/{id}/cancel', desc: 'Hủy booking', dtoIn: 'CancelBookingRequestDto { lyDo, expectedStatus }', dtoOut: '{ message }' },
  { method: 'PATCH', path: '/api/bookings/{id}/confirm-completion', desc: 'Chủ nuôi xác nhận hoàn tất booking', dtoIn: 'None', dtoOut: '{ message, booking: BookingDetailDto }' },
  { method: 'PATCH', path: '/api/bookings/{id}/dispute', desc: 'Chủ nuôi khiếu nại booking (COMPLETION_PENDING -> DISPUTED)', dtoIn: 'DisputeBookingDto { lyDo }', dtoOut: '{ message, booking: BookingDetailDto }' },
  { method: 'POST', path: '/api/bookings/{id}/review', desc: 'Đánh giá booking (chỉ khi COMPLETED)', dtoIn: 'CreateReviewDto { rating, comment }', dtoOut: 'ReviewDto' },

  { method: 'GET', path: '/api/sitter/bookings', desc: 'Lấy bookings của sitter', dtoIn: 'query: ?status=', dtoOut: 'List<BookingDetailDto>' },
  { method: 'PATCH', path: '/api/sitter/bookings/{id}/accept', desc: 'Chấp nhận booking', dtoIn: 'None', dtoOut: '{ message }' },
  { method: 'PATCH', path: '/api/sitter/bookings/{id}/reject', desc: 'Từ chối booking', dtoIn: 'RejectBookingDto { lyDo }', dtoOut: '{ message }' },
  { method: 'PATCH', path: '/api/sitter/bookings/{id}/complete', desc: 'Sitter đánh dấu hoàn tất (ACCEPTED -> COMPLETION_PENDING)', dtoIn: 'None', dtoOut: '{ message, booking: BookingDetailDto }' },

  { method: 'GET', path: '/api/sitter/profile', desc: 'Lấy profile sitter', dtoIn: 'None', dtoOut: 'SitterDetailDto' },
  { method: 'PUT', path: '/api/sitter/profile', desc: 'Cập nhật profile sitter', dtoIn: 'UpdateSitterProfileDto { bio, giaMoiNgay, khuVuc, loaiPetNhan, tags, dichVuBoSung }', dtoOut: '{ message }' },
  { method: 'GET', path: '/api/sitter/availability', desc: 'Lấy lịch rảnh sitter theo tháng', dtoIn: 'query: ?month=YYYY-MM', dtoOut: 'List<SitterDayAvailabilityDto>' },
  { method: 'PUT', path: '/api/sitter/availability', desc: 'Cập nhật lịch rảnh sitter', dtoIn: 'UpdateSitterAvailabilityDto { dates, isAvailable }', dtoOut: '{ message }' },

  { method: 'GET', path: '/api/sitters', desc: 'Danh sách sitter public', dtoIn: 'query: ?khuVuc=&loaiPet=&page=&pageSize=', dtoOut: 'List<SitterListDto>' },
  { method: 'GET', path: '/api/sitters/featured', desc: 'Sitter nổi bật', dtoIn: 'None', dtoOut: 'List<SitterListDto>' },
  { method: 'GET', path: '/api/sitters/{id}', desc: 'Chi tiết sitter', dtoIn: 'None', dtoOut: 'SitterDetailDto' },
  { method: 'GET', path: '/api/sitters/{id}/reviews', desc: 'Đánh giá của sitter', dtoIn: 'None', dtoOut: 'List<ReviewDto>' },
  { method: 'GET', path: '/api/sitters/{id}/availability', desc: 'Lịch rảnh sitter public', dtoIn: 'query: ?month=YYYY-MM', dtoOut: 'List<string>' },
  { method: 'GET', path: '/api/services', desc: 'Danh sách dịch vụ chung', dtoIn: 'None', dtoOut: 'List<ServiceDto>' },
  { method: 'GET', path: '/api/stats', desc: 'Thống kê tổng quan', dtoIn: 'None', dtoOut: 'StatsDto' },
  { method: 'GET', path: '/api/reviews', desc: 'Top đánh giá public (mục B.6 cần thêm)', dtoIn: 'None', dtoOut: 'List<PublicReviewDto>' },

  { method: 'GET', path: '/api/admin/sitter-applications', desc: 'Đơn đăng ký sitter', dtoIn: 'query: ?status=', dtoOut: 'List<SitterApplicationDto>' },
  { method: 'PATCH', path: '/api/admin/sitter-applications/{id}/approve', desc: 'Duyệt đơn sitter', dtoIn: 'None', dtoOut: '{ message }' },
  { method: 'PATCH', path: '/api/admin/sitter-applications/{id}/reject', desc: 'Từ chối đơn sitter', dtoIn: 'RejectApplicationDto { lyDo }', dtoOut: '{ message }' },
  { method: 'GET', path: '/api/admin/disputed-bookings', desc: 'Danh sách đơn đặt lịch đang khiếu nại (DISPUTED)', dtoIn: 'None', dtoOut: 'List<BookingDetailDto>' },
  { method: 'PATCH', path: '/api/admin/bookings/{id}/resolve', desc: 'Admin xử lý tranh chấp đơn khiếu nại', dtoIn: 'ResolveDisputeDto { resolution, note }', dtoOut: '{ message, booking: BookingDetailDto }' },

  { method: 'GET', path: '/api/pets', desc: 'Danh sách pet của user', dtoIn: 'None', dtoOut: 'List<PetDto>' },
  { method: 'POST', path: '/api/pets', desc: 'Tạo pet mới', dtoIn: 'CreatePetDto { ten, loai, giong, canNang, tuoi, ghiChu, avatarUrl }', dtoOut: 'PetDto' },
  { method: 'PUT', path: '/api/pets/{id}', desc: 'Cập nhật pet', dtoIn: 'CreatePetDto', dtoOut: 'PetDto' },
  { method: 'DELETE', path: '/api/pets/{id}', desc: 'Xóa pet', dtoIn: 'None', dtoOut: 'NoContent' },

  { method: 'GET', path: '/api/sitter-applications/me', desc: 'Đơn đăng ký của tôi', dtoIn: 'None', dtoOut: 'SitterApplicationDto' },
  { method: 'POST', path: '/api/sitter-applications', desc: 'Nộp đơn đăng ký sitter', dtoIn: 'CreateSitterApplicationDto', dtoOut: '{ message }' },

  { method: 'POST', path: '/api/sitter/bookings/{id}/updates', desc: 'Thêm cập nhật chăm sóc', dtoIn: 'FormData { note, photos }', dtoOut: 'StayUpdateDto' },
  { method: 'DELETE', path: '/api/sitter/bookings/{bookingId}/updates/{updateId}', desc: 'Xóa cập nhật', dtoIn: 'None', dtoOut: '{ message }' },
  { method: 'GET', path: '/api/bookings/{id}/updates', desc: 'Danh sách cập nhật chăm sóc', dtoIn: 'None', dtoOut: 'List<StayUpdateDto>' },

  { method: 'POST', path: '/api/contact', desc: 'Gửi liên hệ', dtoIn: 'ContactDto { name, email, subject, message, website }', dtoOut: '{ message }' },
  { method: 'POST', path: '/api/newsletter', desc: 'Đăng ký bản tin', dtoIn: 'NewsletterDto { email }', dtoOut: '{ message }' },
  { method: 'GET', path: '/api/admin/messages', desc: 'Danh sách tin nhắn liên hệ', dtoIn: 'query: ?isRead=', dtoOut: 'List<ContactMessage>' },
  { method: 'PATCH', path: '/api/admin/messages/{id}/read', desc: 'Đánh dấu đã đọc tin nhắn', dtoIn: 'None', dtoOut: '{ message }' }
];

fs.writeFileSync(path.resolve(__dirname, 'backend_endpoints.json'), JSON.stringify(backendEndpoints, null, 2), 'utf8');
console.log(`Backend endpoints: ${backendEndpoints.length}`);
