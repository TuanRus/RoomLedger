# Frontend (Angular) — Kỉu

Thư mục này mới có khung. Tạo app Angular ngay tại đây:

```bash
cd src/frontend
npx @angular/cli@18 new roomledger-web --directory . --routing --style=scss --standalone --skip-git
```

Chọn "Overwrite" nếu được hỏi về README. Giữ nguyên các thư mục trong `src/app/` bên dưới.

```
src/app/
├── core/        # auth (Cognito), interceptor, guard theo vai, api client
├── shared/      # component dùng chung
├── features/
│   ├── admin/     # overview, audit-log, ai-config
│   ├── landlord/  # rooms, handover, invoices, checkout
│   └── tenant/    # home, upload, confirm, checkout
└── models/      # sinh từ src/contracts/openapi.yaml
```
