// ================================================
// PAWSTAY - Hệ thống dữ liệu trung tâm (data.js)
// Phiên bản: 2.0 (Nâng cấp toàn diện)
//
// Cấu trúc:
//   SITTERS_DATA    — Danh sách 12 pet-sitter
//   REVIEWS_DATA    — Danh sách đánh giá (liên kết với sitter id)
//   FAQ_DATA        — Câu hỏi thường gặp
//   STATS           — Số liệu thống kê trang chủ
//   ADDONS          — Dịch vụ bổ sung khi đặt lịch
//
// Để chỉnh sửa: tìm section tương ứng bên dưới
// ================================================

// ──────────────────────────────────────────────
// 1. DANH SÁCH PET-SITTER (12 người)
// ──────────────────────────────────────────────
const SITTERS_DATA = [
  {
    id: 1,
    ten: "Nguyễn Thị Lan",
    gioiTinh: "female",
    tuoi: 26,
    anh: "assets/images/sitter1.jpg",
    khuVuc: "Hải Châu",
    diaChiDay: "Đường Trần Phú, Hải Châu, Đà Nẵng",
    giaMotNgay: 200000,
    rating: 4.9,
    soLuotNhan: 47,
    soReview: 38,
    moTa: "Yêu thú cưng từ nhỏ, có kinh nghiệm 3 năm chăm sóc chó mèo tại nhà. Tôi coi mỗi bé như thành viên trong gia đình.",
    bioDay: "Tôi là Lan, 26 tuổi, hiện đang sống tại Hải Châu. Lớn lên cùng 3 chú chó và 2 chú mèo, tình yêu thú cưng của tôi được hình thành từ rất sớm. Tôi đã từng làm tình nguyện viên tại trại cứu hộ thú cưng Đà Nẵng trong 1 năm. Khi nhận trông thú cưng của bạn, tôi luôn cập nhật ảnh mỗi sáng và chiều.",
    kinhNghiem: "3 năm",
    loaiPet: ["Chó", "Mèo"],
    xacMinh: true,
    noi: true,  // Nhận trông tại nhà sitter
    den: true,  // Đến nhà chủ pet
    avatarColor: "#e8b4b8",
    tags: ["Nhiệt tình", "Báo cáo đầy đủ", "Nhà sạch sẽ"],
    lichRanh: ["2024-02-01","2024-02-02","2024-02-05","2024-02-06","2024-02-10","2024-02-11","2024-02-15"],
    noiBat: true  // Hiển thị trong "Sitter nổi bật" trang chủ
  },
  {
    id: 2,
    ten: "Trần Minh Khoa",
    gioiTinh: "male",
    tuoi: 29,
    anh: "assets/images/sitter2.jpg",
    khuVuc: "Sơn Trà",
    diaChiDay: "Đường Hoàng Sa, Sơn Trà, Đà Nẵng",
    giaMotNgay: 180000,
    rating: 4.8,
    soLuotNhan: 32,
    soReview: 26,
    moTa: "Nhà rộng, có sân vườn. Chuyên trông chó lớn và chó năng động, yêu chạy nhảy.",
    bioDay: "Chào bạn! Mình là Khoa, sống tại khu vực Sơn Trà gần biển. Nhà mình có sân vườn rộng ~50m², rất phù hợp cho các bé chó cần không gian vận động. Mình chuyên nhận trông chó, đặc biệt là chó lớn và các giống hay năng động như Husky, Labrador, Golden.",
    kinhNghiem: "2 năm",
    loaiPet: ["Chó"],
    xacMinh: true,
    noi: true,
    den: false,
    avatarColor: "#a8c5da",
    tags: ["Có sân vườn", "Chó lớn OK", "Gần biển"],
    lichRanh: ["2024-02-03","2024-02-04","2024-02-08","2024-02-09","2024-02-12","2024-02-13"],
    noiBat: true
  },
  {
    id: 3,
    ten: "Lê Thị Hoa",
    gioiTinh: "female",
    tuoi: 32,
    anh: "assets/images/sitter3.jpg",
    khuVuc: "Thanh Khê",
    diaChiDay: "Đường Điện Biên Phủ, Thanh Khê, Đà Nẵng",
    giaMotNgay: 150000,
    rating: 5.0,
    soLuotNhan: 61,
    soReview: 55,
    moTa: "Từng làm kỹ thuật viên thú y 2 năm. Biết sơ cứu cơ bản và chăm sóc pet đang ốm.",
    bioDay: "Mình là Hoa, trước đây từng làm kỹ thuật viên tại phòng khám thú y ở Đà Nẵng 2 năm. Điều đó giúp mình có thể nhận biết khi bé không khoẻ và sơ cứu đúng cách. Hiện mình đang làm pet-sitter full-time, nhận cả các bé đang điều trị dài hạn cần uống thuốc đúng giờ.",
    kinhNghiem: "4 năm",
    loaiPet: ["Chó", "Mèo", "Khác"],
    xacMinh: true,
    noi: true,
    den: true,
    avatarColor: "#b8d8b8",
    tags: ["Có kinh nghiệm thú y", "Nhận pet ốm", "Full-time sitter"],
    lichRanh: ["2024-02-01","2024-02-07","2024-02-08","2024-02-14","2024-02-15"],
    noiBat: true
  },
  {
    id: 4,
    ten: "Phạm Đức Anh",
    gioiTinh: "male",
    tuoi: 27,
    anh: "assets/images/sitter4.jpg",
    khuVuc: "Liên Chiểu",
    diaChiDay: "Đường Nguyễn Lương Bằng, Liên Chiểu, Đà Nẵng",
    giaMotNgay: 170000,
    rating: 4.7,
    soLuotNhan: 28,
    soReview: 22,
    moTa: "Freelancer làm từ nhà, có thể theo dõi pet cả ngày. Cập nhật ảnh/video đều đặn.",
    bioDay: "Mình là Đức Anh, làm lập trình viên freelance tại nhà nên luôn ở nhà 24/7. Điều đó nghĩa là pet của bạn sẽ không bao giờ bị bỏ mình một mình. Mình sẽ livestream hoặc gửi clip cho bạn bất cứ lúc nào bạn muốn check-in.",
    kinhNghiem: "1.5 năm",
    loaiPet: ["Chó", "Mèo"],
    xacMinh: true,
    noi: true,
    den: false,
    avatarColor: "#d4b8e0",
    tags: ["Ở nhà 24/7", "Cập nhật video", "Giá tốt"],
    lichRanh: ["2024-02-02","2024-02-03","2024-02-09","2024-02-10","2024-02-16"],
    noiBat: false
  },
  {
    id: 5,
    ten: "Võ Thị Mai",
    gioiTinh: "female",
    tuoi: 24,
    anh: "assets/images/sitter5.jpg",
    khuVuc: "Ngũ Hành Sơn",
    diaChiDay: "Đường Trường Sa, Ngũ Hành Sơn, Đà Nẵng",
    giaMotNgay: 250000,
    rating: 4.9,
    soLuotNhan: 53,
    soReview: 49,
    moTa: "Chuyên chăm mèo và thú cưng nhỏ. Nhà thoáng mát, an toàn, có camera giám sát.",
    bioDay: "Mình là Mai, chuyên trông mèo và các bé pet nhỏ (thỏ, hamster, chim...). Nhà mình thiết kế đặc biệt cho mèo: có cây leo, khu vui chơi, và camera 24h để bạn theo dõi bất cứ lúc nào. Mình cũng cung cấp dịch vụ tắm mèo, cắt móng tại nhà.",
    kinhNghiem: "2.5 năm",
    loaiPet: ["Mèo", "Khác"],
    xacMinh: true,
    noi: true,
    den: true,
    avatarColor: "#f0d4a8",
    tags: ["Chuyên mèo", "Camera 24h", "Cắt móng"],
    lichRanh: ["2024-02-04","2024-02-05","2024-02-11","2024-02-12","2024-02-18","2024-02-19"],
    noiBat: true
  },
  {
    id: 6,
    ten: "Đinh Văn Hùng",
    gioiTinh: "male",
    tuoi: 34,
    anh: "assets/images/sitter6.jpg",
    khuVuc: "Cẩm Lệ",
    diaChiDay: "Đường Cách Mạng Tháng 8, Cẩm Lệ, Đà Nẵng",
    giaMotNgay: 300000,
    rating: 4.8,
    soLuotNhan: 39,
    soReview: 35,
    moTa: "Pet-sitter chuyên nghiệp, đã qua khóa huấn luyện chăm sóc thú cưng do Hội Thú Y cấp.",
    bioDay: "Tôi là Hùng, pet-sitter chuyên nghiệp với chứng chỉ chăm sóc thú cưng từ Hội Thú Y Việt Nam. Tôi cũng là người huấn luyện chó nghiệp dư — nếu bé nhà bạn có vấn đề hành vi, tôi có thể hỗ trợ thêm. Giá cao hơn nhưng chất lượng dịch vụ chuyên nghiệp 100%.",
    kinhNghiem: "5 năm",
    loaiPet: ["Chó", "Mèo"],
    xacMinh: true,
    noi: true,
    den: true,
    avatarColor: "#c8d8e8",
    tags: ["Có chứng chỉ", "Huấn luyện chó", "Chuyên nghiệp"],
    lichRanh: ["2024-02-06","2024-02-07","2024-02-13","2024-02-14","2024-02-20"],
    noiBat: true
  },
  {
    id: 7,
    ten: "Nguyễn Thảo Vy",
    gioiTinh: "female",
    tuoi: 22,
    anh: "assets/images/sitter7.jpg",
    khuVuc: "Hải Châu",
    diaChiDay: "Đường Lê Duẩn, Hải Châu, Đà Nẵng",
    giaMotNgay: 160000,
    rating: 4.6,
    soLuotNhan: 18,
    soReview: 15,
    moTa: "Sinh viên ngành Thú y năm 4, yêu động vật, giá hợp lý cho sinh viên và bạn trẻ.",
    bioDay: "Mình là Thảo Vy, sinh viên năm 4 khoa Thú y tại Đại học Nông Lâm. Kiến thức chuyên môn từ trường kết hợp tình yêu thú cưng thực tế giúp mình chăm sóc bé nhà bạn đúng cách. Giá mình dễ chịu, phù hợp cho sinh viên và gia đình trẻ.",
    kinhNghiem: "1 năm",
    loaiPet: ["Chó", "Mèo"],
    xacMinh: true,
    noi: true,
    den: false,
    avatarColor: "#fcd4e8",
    tags: ["Sinh viên thú y", "Giá sinh viên", "Trẻ nhiệt tình"],
    lichRanh: ["2024-02-01","2024-02-08","2024-02-15","2024-02-16","2024-02-22"],
    noiBat: false
  },
  {
    id: 8,
    ten: "Bùi Quang Minh",
    gioiTinh: "male",
    tuoi: 31,
    anh: "assets/images/sitter8.jpg",
    khuVuc: "Sơn Trà",
    diaChiDay: "Đường Võ Nguyên Giáp, Sơn Trà, Đà Nẵng",
    giaMotNgay: 220000,
    rating: 4.8,
    soLuotNhan: 44,
    soReview: 40,
    moTa: "Runner & dog lover. Đưa chó đi chạy bộ mỗi sáng tại bãi biển Mỹ Khê — bé nào cũng thích!",
    bioDay: "Mình là Quang Minh, runner nghiệp dư với 5 năm kinh nghiệm chạy bộ. Mỗi sáng mình đều dậy 5h30 chạy bộ dọc biển Mỹ Khê ~5km. Nếu bé nhà bạn cần nhiều vận động, đây là địa chỉ hoàn hảo. Mình cũng sẽ chia sẻ clip bé chạy vui vẻ mỗi ngày!",
    kinhNghiem: "3 năm",
    loaiPet: ["Chó"],
    xacMinh: true,
    noi: true,
    den: true,
    avatarColor: "#b8e0d4",
    tags: ["Đưa đi chạy bộ", "Gần biển", "Năng động"],
    lichRanh: ["2024-02-02","2024-02-03","2024-02-09","2024-02-16","2024-02-17"],
    noiBat: true
  },
  {
    id: 9,
    ten: "Trần Thị Bích",
    gioiTinh: "female",
    tuoi: 38,
    anh: "assets/images/sitter9.jpg",
    khuVuc: "Thanh Khê",
    diaChiDay: "Đường Ngô Quyền, Thanh Khê, Đà Nẵng",
    giaMotNgay: 190000,
    rating: 4.9,
    soLuotNhan: 72,
    soReview: 65,
    moTa: "Mẹ 2 con, yêu thú cưng và có kinh nghiệm chăm sóc cả chó mèo lẫn các bé đặc biệt.",
    bioDay: "Mình là Bích, mẹ của 2 bé 5 tuổi và 8 tuổi. Các con mình lớn lên cùng thú cưng nên nhà mình luôn thân thiện với mọi loài. Mình đặc biệt kiên nhẫn với các bé nhút nhát, mới đến môi trường lạ. Thú cưng của bạn sẽ cảm thấy như ở nhà từ ngày đầu tiên.",
    kinhNghiem: "4 năm",
    loaiPet: ["Chó", "Mèo", "Khác"],
    xacMinh: true,
    noi: true,
    den: false,
    avatarColor: "#e8d4b8",
    tags: ["Gia đình thân thiện", "Kiên nhẫn", "Nhiều kinh nghiệm"],
    lichRanh: ["2024-02-05","2024-02-06","2024-02-12","2024-02-13","2024-02-19","2024-02-20"],
    noiBat: false
  },
  {
    id: 10,
    ten: "Lê Hoàng Nam",
    gioiTinh: "male",
    tuoi: 25,
    anh: "assets/images/sitter10.jpg",
    khuVuc: "Liên Chiểu",
    diaChiDay: "Đường Tôn Đức Thắng, Liên Chiểu, Đà Nẵng",
    giaMotNgay: 160000,
    rating: 4.7,
    soLuotNhan: 23,
    soReview: 18,
    moTa: "Nhân viên văn phòng với giờ làm linh hoạt. Có em gái ở nhà hỗ trợ trong giờ làm việc.",
    bioDay: "Mình là Hoàng Nam, làm nhân viên IT với lịch làm việc remote linh hoạt. Em gái mình (19 tuổi, cũng yêu thú cưng) sẽ ở nhà hỗ trợ trong những giờ mình cần đến văn phòng. Vậy là pet của bạn luôn có người ở nhà 24/7.",
    kinhNghiem: "1.5 năm",
    loaiPet: ["Chó", "Mèo"],
    xacMinh: true,
    noi: true,
    den: false,
    avatarColor: "#d4e8c8",
    tags: ["Remote work", "Luôn có người ở nhà", "Giá tốt"],
    lichRanh: ["2024-02-04","2024-02-07","2024-02-11","2024-02-18","2024-02-21"],
    noiBat: false
  },
  {
    id: 11,
    ten: "Phạm Thị Quỳnh",
    gioiTinh: "female",
    tuoi: 29,
    anh: "assets/images/sitter11.jpg",
    khuVuc: "Ngũ Hành Sơn",
    diaChiDay: "Đường Lê Văn Hiến, Ngũ Hành Sơn, Đà Nẵng",
    giaMotNgay: 270000,
    rating: 4.9,
    soLuotNhan: 58,
    soReview: 52,
    moTa: "Chuyên chăm sóc pet cao cấp: dịch vụ spa, chụp ảnh nghệ thuật, diet plan riêng.",
    bioDay: "Mình là Quỳnh, chuyên cung cấp dịch vụ pet-care cao cấp. Ngoài trông giữ thông thường, mình còn cung cấp: spa tắm gội, chụp ảnh nghệ thuật cho bé, lên thực đơn ăn uống theo cân nặng và sức khỏe. Mỗi bé đều có 'nhật ký chăm sóc' riêng được cập nhật hàng ngày.",
    kinhNghiem: "3 năm",
    loaiPet: ["Chó", "Mèo"],
    xacMinh: true,
    noi: true,
    den: true,
    avatarColor: "#e8c8e8",
    tags: ["Pet spa", "Chụp ảnh nghệ thuật", "Nhật ký hàng ngày"],
    lichRanh: ["2024-02-01","2024-02-08","2024-02-09","2024-02-15","2024-02-22"],
    noiBat: false
  },
  {
    id: 12,
    ten: "Ngô Văn Thành",
    gioiTinh: "male",
    tuoi: 45,
    anh: "assets/images/sitter12.jpg",
    khuVuc: "Cẩm Lệ",
    diaChiDay: "Đường Ông Ích Khiêm, Cẩm Lệ, Đà Nẵng",
    giaMotNgay: 140000,
    rating: 4.6,
    soLuotNhan: 35,
    soReview: 29,
    moTa: "Hưu trí, ở nhà cả ngày. Yêu thương thú cưng như con cái, chăm chút từng bữa ăn.",
    bioDay: "Tôi là Thành, 45 tuổi, vừa nghỉ hưu sớm. Bây giờ tôi có rất nhiều thời gian rảnh và tình yêu thương dành cho thú cưng. Tôi sẽ chăm sóc bé nhà bạn như chăm sóc thú cưng của chính mình — từng bữa ăn đúng giờ, từng giờ ngủ đúng chỗ. Giá hợp lý cho những ai cần người thật sự tận tâm.",
    kinhNghiem: "6 năm",
    loaiPet: ["Chó", "Mèo", "Khác"],
    xacMinh: false, // Đang trong quá trình xác minh
    noi: true,
    den: false,
    avatarColor: "#d4d4b8",
    tags: ["Hưu trí tận tâm", "Giá rẻ nhất", "Ở nhà cả ngày"],
    lichRanh: ["2024-02-01","2024-02-02","2024-02-03","2024-02-04","2024-02-05"],
    noiBat: false
  }
];

// ──────────────────────────────────────────────
// 2. DỮ LIỆU ĐÁNH GIÁ (Reviews)
// Mỗi review liên kết với sitter qua sitterId
// ──────────────────────────────────────────────
const REVIEWS_DATA = [
  // --- Sitter 1: Nguyễn Thị Lan ---
  { id: 101, sitterId: 1, tenKhach: "Minh Tâm", avatar: "MT", rating: 5, ngay: "2024-01-15", noiDung: "Lan rất tận tâm! Gửi ảnh sáng chiều đều đặn. Bé Golden của tôi còn không muốn về nhà 😂", loaiPet: "Chó (Golden)" },
  { id: 102, sitterId: 1, tenKhach: "Thu Hà", avatar: "TH", rating: 5, ngay: "2024-01-08", noiDung: "Nhà sạch sẽ, thoáng mát. Bé mèo ăn ngoan, ngủ ngoan. Rất an tâm khi giao cho chị Lan.", loaiPet: "Mèo (British)" },
  { id: 103, sitterId: 1, tenKhach: "Hoàng Long", avatar: "HL", rating: 5, ngay: "2023-12-28", noiDung: "Lần đầu gửi pet ngoài nhà mà rất an tâm. Chị Lan phản hồi tin nhắn rất nhanh!", loaiPet: "Chó (Corgi)" },
  { id: 104, sitterId: 1, tenKhach: "Phương Linh", avatar: "PL", rating: 4, ngay: "2023-12-10", noiDung: "Nhìn chung tốt, bé ăn đầy đủ. Trừ đúng 1 ngày chị bận không gửi ảnh, nhắc mới gửi.", loaiPet: "Chó (Poodle)" },
  // --- Sitter 2: Trần Minh Khoa ---
  { id: 201, sitterId: 2, tenKhach: "Anh Tuấn", avatar: "AT", rating: 5, ngay: "2024-01-20", noiDung: "Sân vườn rộng rãi, Husky nhà tôi chạy thoải mái suốt ngày. Khoa rất hiểu tâm lý chó lớn!", loaiPet: "Chó (Husky)" },
  { id: 202, sitterId: 2, tenKhach: "Ngọc Mai", avatar: "NM", rating: 5, ngay: "2024-01-12", noiDung: "Gửi 2 tuần, bé Golden tăng cân luôn vì được ăn ngon 😂 Rất hài lòng!", loaiPet: "Chó (Golden)" },
  { id: 203, sitterId: 2, tenKhach: "Bảo Châu", avatar: "BC", rating: 4, ngay: "2023-12-22", noiDung: "Khoa tốt tính, nhiệt tình. Chỉ hơi khó liên lạc buổi tối nhưng ban ngày phản hồi nhanh.", loaiPet: "Chó (Labrador)" },
  // --- Sitter 3: Lê Thị Hoa ---
  { id: 301, sitterId: 3, tenKhach: "Khánh An", avatar: "KA", rating: 5, ngay: "2024-01-25", noiDung: "Bé của tôi bị bệnh đang dùng thuốc. Chị Hoa cho uống thuốc đúng giờ, tỉ mỉ từng li từng tí. 5 sao không đủ!", loaiPet: "Mèo (đang điều trị)" },
  { id: 302, sitterId: 3, tenKhach: "Thu Phương", avatar: "TP", rating: 5, ngay: "2024-01-18", noiDung: "Chị Hoa có chuyên môn thú y rất rõ ràng. Bé có vẻ mệt ngày 2, chị phát hiện sớm và xử lý kịp.", loaiPet: "Chó (Chihuahua)" },
  { id: 303, sitterId: 3, tenKhach: "Minh Đức", avatar: "MD", rating: 5, ngay: "2024-01-05", noiDung: "Tin tưởng tuyệt đối! Bé thỏ nhà mình mà cũng nhận. Hiếm lắm.", loaiPet: "Thỏ" },
  // --- Sitter 5: Võ Thị Mai ---
  { id: 501, sitterId: 5, tenKhach: "Lan Anh", avatar: "LA", rating: 5, ngay: "2024-01-22", noiDung: "Phòng dành cho mèo của chị Mai như resort thu nhỏ. Bé nhà mình khoái lắm, cứ ngồi lì trên cây leo cả ngày.", loaiPet: "Mèo (Maine Coon)" },
  { id: 502, sitterId: 5, tenKhach: "Huy Đăng", avatar: "HD", rating: 5, ngay: "2024-01-14", noiDung: "Camera 24h là một điểm cộng khổng lồ. Tôi có thể xem bé bất cứ lúc nào, rất yên tâm khi đi công tác xa.", loaiPet: "Mèo (Persian)" },
  { id: 503, sitterId: 5, tenKhach: "Yến Nhi", avatar: "YN", rating: 5, ngay: "2024-01-03", noiDung: "Dịch vụ tắm và cắt móng của chị Mai chuyên nghiệp lắm. Bé về nhà thơm tho sạch sẽ, bộ lông bóng mượt.", loaiPet: "Mèo (Scottish)" },
  // --- Sitter 6: Đinh Văn Hùng ---
  { id: 601, sitterId: 6, tenKhach: "Thanh Tùng", avatar: "TT", rating: 5, ngay: "2024-01-19", noiDung: "Anh Hùng có kinh nghiệm và chứng chỉ thật sự. Bé Husky bướng bỉnh của tôi mà anh dạy được ngồi và nằm trong 5 ngày!", loaiPet: "Chó (Husky)" },
  { id: 602, sitterId: 6, tenKhach: "Quỳnh Như", avatar: "QN", rating: 5, ngay: "2024-01-11", noiDung: "Giá cao hơn nhưng hoàn toàn xứng đáng. Dịch vụ chuyên nghiệp từ A đến Z.", loaiPet: "Chó (German Shepherd)" },
  // --- Sitter 8: Bùi Quang Minh ---
  { id: 801, sitterId: 8, tenKhach: "Dương Khang", avatar: "DK", rating: 5, ngay: "2024-01-21", noiDung: "Bé Labrador nhà tôi được đi biển mỗi sáng! Về nhà nó vui vẻ, khỏe mạnh hơn hẳn. 10 điểm không có nhưng nếu có thì tôi cho 10!", loaiPet: "Chó (Labrador)" },
  { id: 802, sitterId: 8, tenKhach: "Hà Linh", avatar: "HL", rating: 5, ngay: "2024-01-09", noiDung: "Clip bé chạy trên bãi biển mỗi sáng đẹp như phim. Minh còn biết chụp ảnh nữa, quá tuyệt!", loaiPet: "Chó (Golden)" }
];

// ──────────────────────────────────────────────
// 3. CÂU HỎI THƯỜNG GẶP (FAQ)
// ──────────────────────────────────────────────
const FAQ_DATA = [
  {
    id: 1,
    nhom: "Về PawStay",
    cauHoi: "PawStay là gì và hoạt động như thế nào?",
    traLoi: "PawStay là nền tảng kết nối chủ thú cưng với pet-sitter uy tín tại Đà Nẵng. Bạn tìm sitter phù hợp, đặt lịch trực tuyến, và thú cưng của bạn sẽ được chăm sóc tại nhà sitter hoặc tại nhà bạn. Mọi giao dịch đều qua PawStay để đảm bảo an toàn."
  },
  {
    id: 2,
    nhom: "Về PawStay",
    cauHoi: "PawStay có hoạt động ở đâu?",
    traLoi: "Hiện tại PawStay đang phục vụ tại Đà Nẵng, bao gồm các quận: Hải Châu, Sơn Trà, Thanh Khê, Liên Chiểu, Ngũ Hành Sơn, Cẩm Lệ và huyện Hòa Vang. Chúng tôi sẽ mở rộng ra Hội An và Huế vào cuối năm 2024."
  },
  {
    id: 3,
    nhom: "Về sitter",
    cauHoi: "Sitter trên PawStay được kiểm tra như thế nào?",
    traLoi: "Mỗi sitter phải qua 3 bước: (1) Xác minh CCCD/hộ khẩu, (2) Phỏng vấn online với nhóm PawStay, (3) Xem xét lịch sử thú cưng cá nhân. Chỉ ~60% người đăng ký được chấp thuận. Badge 'Đã xác minh' chỉ hiển thị khi qua đủ 3 bước."
  },
  {
    id: 4,
    nhom: "Về sitter",
    cauHoi: "Nếu sitter không phản hồi hoặc có vấn đề thì sao?",
    traLoi: "Đội ngũ PawStay hỗ trợ 7 ngày/tuần qua Zalo và hotline. Nếu sitter không phản hồi trong 2 giờ, chúng tôi sẽ hỗ trợ tìm sitter thay thế ngay. Mọi khiếu nại đều được xử lý trong 24 giờ."
  },
  {
    id: 5,
    nhom: "Thanh toán",
    cauHoi: "Thanh toán hoạt động như thế nào?",
    traLoi: "Tiền của bạn được giữ trong tài khoản escrow của PawStay, chỉ chuyển cho sitter sau khi dịch vụ hoàn thành. Nếu sitter huỷ hoặc có vấn đề nghiêm trọng, bạn được hoàn tiền 100% trong 24 giờ."
  },
  {
    id: 6,
    nhom: "Thanh toán",
    cauHoi: "Phí dịch vụ của PawStay là bao nhiêu?",
    traLoi: "PawStay thu phí dịch vụ 10% trên tổng giá trị đặt lịch. Phí này bao gồm: bảo hiểm thú cưng cơ bản trong thời gian gửi, hỗ trợ 24/7, và bảo đảm hoàn tiền nếu có sự cố."
  },
  {
    id: 7,
    nhom: "Đặt lịch",
    cauHoi: "Tôi có thể huỷ lịch không?",
    traLoi: "Có. Nếu huỷ trước 48 giờ: hoàn tiền 100%. Huỷ trước 24 giờ: hoàn tiền 50%. Huỷ trong vòng 24 giờ trước ngày gửi: hoàn tiền 0% (để đảm bảo sitter không bị thiệt hại)."
  },
  {
    id: 8,
    nhom: "Đặt lịch",
    cauHoi: "Tôi có thể đặt lịch cho nhiều thú cưng cùng lúc không?",
    traLoi: "Có! Bạn có thể ghi chú số lượng và loại thú cưng trong phần 'Ghi chú đặc biệt' khi đặt lịch. Sitter sẽ báo giá thêm (nếu có) cho con thứ 2 trở đi — thường thêm 50% giá bình thường."
  }
];

// ──────────────────────────────────────────────
// 4. SỐ LIỆU THỐNG KÊ (Stats)
// Dùng trong trang chủ
// ──────────────────────────────────────────────
const STATS = {
  tongDatLich:   1642,
  siTterXacMinh: 68,
  tiLHaiLong:    98,
  namHoatDong:   2,
  thanhPho:       1
};

// ──────────────────────────────────────────────
// 5. DỊCH VỤ BỔ SUNG (Add-ons) khi đặt lịch
// ──────────────────────────────────────────────
const ADDONS = [
  {
    id: "tau-goi",
    ten: "Tắm gội & sấy khô",
    gia: 50000,
    icon: "🛁",
    moTa: "Tắm gội sạch sẽ, sấy khô và chải lông. Áp dụng 1 lần/đợt gửi."
  },
  {
    id: "di-kham",
    ten: "Đưa đi khám thú y",
    gia: 100000,
    icon: "🏥",
    moTa: "Sitter đưa bé đến phòng khám thú y gần nhất (không bao gồm chi phí khám)."
  },
  {
    id: "anh-ngay",
    ten: "Bộ ảnh hàng ngày",
    gia: 30000,
    icon: "📸",
    moTa: "Bộ 5-10 ảnh chất lượng cao mỗi ngày, được chỉnh màu cơ bản trước khi gửi."
  },
  {
    id: "giu-thuoc",
    ten: "Cho uống thuốc theo toa",
    gia: 20000,
    icon: "💊",
    moTa: "Sitter cho bé uống thuốc theo lịch bác sĩ kê (tối đa 3 lần/ngày)."
  },
  {
    id: "van-dong",
    ten: "Buổi vận động thêm",
    gia: 40000,
    icon: "🏃",
    moTa: "1 buổi dắt đi bộ/chơi ngoài trời thêm (30-45 phút), ngoài lịch thường."
  }
];

// ──────────────────────────────────────────────
// 6. UTILITY FUNCTIONS (Hàm tiện ích dùng chung)
// ──────────────────────────────────────────────

// Hàm format tiền tệ Việt Nam
function formatTien(so) {
  return so.toLocaleString('vi-VN') + 'đ';
}

// Hàm render sao rating (trả về HTML string)
function renderSao(rating, size) {
  const diem = parseFloat(rating);
  const fs   = size || '0.85rem';
  let html   = `<span class="stars" style="font-size:${fs}">`;
  for (let i = 1; i <= 5; i++) {
    if (i <= Math.floor(diem))          html += '★';
    else if (i - diem < 1 && i - diem > 0) html += '½';
    else html += '<span style="opacity:0.25">★</span>';
  }
  html += '</span>';
  return html;
}

// Hàm tạo initials từ họ tên (VD: "Nguyễn Thị Lan" → "LN")
function getInitials(ten) {
  const parts = ten.trim().split(/\s+/);
  const last  = parts[parts.length - 1].charAt(0);
  const first = parts.length > 1 ? parts[0].charAt(0) : '';
  return last + first;
}

// Hàm lấy reviews theo sitter ID
function getReviewsBySitter(sitterId) {
  return REVIEWS_DATA.filter(r => r.sitterId === sitterId);
}

// Hàm tính ngày giữa 2 mốc
function tinhSoNgay(batDau, ketThuc) {
  const ms = new Date(ketThuc) - new Date(batDau);
  return Math.ceil(ms / (1000 * 60 * 60 * 24));
}

// Hàm hiển thị toast notification
function showToast(message, duration) {
  // Tìm hoặc tạo toast element
  let toast = document.getElementById('global-toast');
  if (!toast) {
    toast = document.createElement('div');
    toast.id = 'global-toast';
    toast.className = 'toast';
    document.body.appendChild(toast);
  }
  toast.textContent = message;
  toast.classList.add('show');
  setTimeout(() => toast.classList.remove('show'), duration || 3000);
}

// ──────────────────────────────────────────────
// 7. AUTH GIẢ LẬP (localStorage)
// ──────────────────────────────────────────────

// Kiểm tra xem user đã "đăng nhập" chưa
function isLoggedIn() {
  return !!localStorage.getItem('pawstay_user');
}

// Lấy thông tin user hiện tại
function getCurrentUser() {
  const raw = localStorage.getItem('pawstay_user');
  return raw ? JSON.parse(raw) : null;
}

// Đặt trạng thái "đăng nhập" (giả lập)
function setLoggedIn(user) {
  localStorage.setItem('pawstay_user', JSON.stringify(user));
}

// Đăng xuất
function logout() {
  localStorage.removeItem('pawstay_user');
  window.location.href = 'index.html';
}

// Lấy lịch sử đặt lịch của user từ localStorage
function getBookingHistory() {
  const raw = localStorage.getItem('pawstay_booking_history');
  return raw ? JSON.parse(raw) : [];
}

// Thêm một booking vào lịch sử
function addToBookingHistory(booking) {
  const history = getBookingHistory();
  history.unshift(booking); // thêm vào đầu mảng
  localStorage.setItem('pawstay_booking_history', JSON.stringify(history));
}

module.exports = { SITTERS_DATA, REVIEWS_DATA, ADDONS };