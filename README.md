# RoomLedger

Ứng dụng quản lý phòng trọ: biên bản nhận phòng có ảnh được khóa bằng S3 Object Lock, hai bên xác nhận, kết thúc hợp đồng và chuyển phòng, ghi điện nước và hợp đồng scan.

## Cấu trúc

```
RoomLedger/
├── .github/            # CI (backend, frontend, infra) và mẫu PR
├── src/
│   ├── backend/        # .NET 8: Api, Core, Infrastructure, Ai, Functions, tests
│   ├── infra/          # AWS CDK (C#), 9 stack
│   ├── frontend/       # Angular
│   ├── contracts/      # openapi.yaml
│   └── evaluation/     # đo AI và thời gian
└── docs/               # tài liệu, chỉ lưu trên máy (.gitignore bỏ qua)
```

## Công nghệ

- Backend: .NET 8, ASP.NET Core trên AWS Lambda, EF Core code first, Amazon RDS for PostgreSQL
- Frontend: Angular
- Hạ tầng: AWS CDK (C#), GitHub Actions (OIDC)

## Fixed technical choices (do not change without a team decision)

| Choice | Value | Why |
|---|---|---|
| .NET | **8** (`net8.0`, Lambda `dotnet8`), not 9 or 10 | Team choice. EF Core / Npgsql must stay on **8.x**: 9.x installs on net8.0 without any error |
| AWS region | **ap-southeast-2 (Sydney)**, not ap-southeast-1 | The AWS Organization's SCP blocks CloudFormation, S3, Lambda, RDS, etc. in ap-southeast-1, us-east-1 and every other region tested (checked 2026-11-05). CDK can't deploy to us-east-1, so `EdgeStack` runs in ap-southeast-2 without a CloudFront WAF (that WAF must live in us-east-1) |
| Bedrock models | `au.anthropic.claude-sonnet-4-5-20250929-v1:0`, fallback `au.anthropic.claude-haiku-4-5-20251001-v1:0` | In Sydney, Claude only works through inference profiles; newer models say "not available for this account" |
| Lambda concurrency | Account limit is **10** (increase requested) | Don't set reserved concurrency until it's raised |
| Toolchain check | `bash scripts/verify-toolchain.sh` must end with `T0 RESULT: GREEN` | Checks SDK 8, dotnet-ef 8 and that no project resolves EF/Npgsql 9.x |

## Deploy lần đầu (làm một lần cho mỗi AWS account)

Chạy từ `src/infra/RoomLedger.Cdk`, region `ap-southeast-2`, bằng tài khoản có quyền admin (chỉ lần đầu; sau đó GitHub Actions deploy qua OIDC, không lưu AWS key trong GitHub).

1. Bootstrap CDK:

   ```bash
   cdk bootstrap aws://<ACCOUNT_ID>/ap-southeast-2
   ```

2. Tạo GitHub OIDC provider. Mỗi account chỉ có **một** provider cho URL `token.actions.githubusercontent.com`, dùng chung cho dev và demo. `SecurityStack` mặc định không tạo provider mà tham chiếu provider có sẵn, nên provider phải có trước khi deploy. Chọn **một** trong hai cách, chỉ làm một lần:

   - Tạo tay (khuyên dùng, không gắn với stage nào):

     ```bash
     aws iam create-open-id-connect-provider --url https://token.actions.githubusercontent.com --client-id-list sts.amazonaws.com
     ```

   - Hoặc để CDK tạo khi deploy **một** stage duy nhất (provider được giữ lại khi `cdk destroy`):

     ```bash
     cdk deploy roomledger-dev-security -c stage=dev -c createGithubOidcProvider=true
     ```

   Không truyền `createGithubOidcProvider=true` cho stage thứ hai: CloudFormation sẽ báo provider đã tồn tại. Kiểm tra: `aws iam list-open-id-connect-providers`.

3. Deploy stage. Stage `demo` và `prod` bắt buộc có `webOrigin` (origin của web, dùng cho CORS của bucket incoming); thiếu, bỏ trống hoặc `*` thì `cdk synth` báo lỗi. Stage `dev` không truyền thì dùng `*`.

   ```bash
   cdk deploy --all -c stage=dev
   cdk deploy --all -c stage=demo -c webOrigin=https://<domain web demo>
   cdk deploy --all -c stage=prod -c webOrigin=https://<domain web prod>
   ```

4. Lấy ARN role `roomledger-{stage}-github-deploy` từ output của `SecurityStack`, gắn vào bước `aws-actions/configure-aws-credentials` (`role-to-assume`) trong `.github/workflows/infra.yml`.

Dọn dẹp: `cdk destroy --all -c stage=<stage>`. Được giữ lại và phải xóa tay: bucket evidence (Object Lock, mọi stage), cả ba bucket ở stage demo và prod, và OIDC provider (chỉ xóa khi không còn stage nào dùng).

## Trạng thái MD-04 Biên bản nhận phòng (Sang, cập nhật 2026-11-11)

| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| MD-04-FE-01 Mở biên bản nhận phòng | ✅ Xong | `POST /tenancies/{id}/inspections/move-in`, `GET /inspections/{id}`; bản sao khu vực lúc mở (migration `Md04_InspectionAreaSnapshot`) |
| MD-04-FE-02 Gửi ảnh | ✅ Xong | Dùng `POST /uploads` của Tuấn (+ `captureSource`); photo-ingest, photo-derive (HEIC → JPEG, thumbnail, xóa GPS) |
| MD-04-FE-03 Xóa ảnh trước khi khóa | ✅ Xong | `DELETE /photos/{id}`; chỉ người gửi, chỉ trước khi khóa (K1) |
| MD-04-FE-04 Xác nhận và khóa, BR-18 RULE 1–4 | ✅ Xong | `POST /inspections/{id}/confirm`; khóa đúng một lần; evidence-seal chép ảnh sang bucket evidence (Object Lock), kiểm SHA-256 |
| MD-04-FE-04 BR-18 RULE 5 Chỉ số điện nước ban đầu | ✅ Xong | `PUT /inspections/{id}/opening-readings` (hai bên nhập, ghi `tenancy.opening_electricity` / `opening_water` của Tuấn ở #13); đổi số → hủy xác nhận; xác nhận cần đủ hai chỉ số (409 `INCOMPLETE_INSPECTION`). Nhập tay, AI đọc ảnh đồng hồ chưa làm |
| MD-04-FE-04 BR-18 RULE 2 Email báo bên còn lại | ❌ **Chưa làm** | API đã phát sự kiện `InspectionConfirmed`; chưa có Lambda `notify` (SES) để gửi email |
| MD-04-FE-05 Xuất PDF biên bản nhận phòng | ⬜ Stretch | Quyết định ngày 25/11 |

**Chưa kiểm trên AWS (T13):** chưa deploy. Cần Network + Data, StorageStack (bucket `roomledger-{stage}-incoming`, `-derived`, `-evidence` có versioning + Object Lock, bật EventBridge cho incoming) và Api/Auth.

## Trả phòng (MD-06 cũ đã gỡ)

Luồng trả phòng cũ (biên bản trả phòng, duyệt khoản trừ cọc, khiếu nại, quyết toán, Lambda `render-pdf`, rule `TenancySettled`) đã bị gỡ khỏi code. Thay bằng kết thúc hợp đồng (`EndTenancyAppService`, hợp đồng ACTIVE → MOVING_OUT → SETTLED) kèm chuyển phòng. Biên bản nhận phòng, `InspectionLocked` → evidence-seal, chỉ số điện nước và hóa đơn giữ nguyên.

- Bảng `finding`, `settlement`, các migration của chúng và cột `inspection.final_period` vẫn nằm trong database và mô hình EF (chưa có migration xóa).
- Báo động chưa gắn SNS/email: cần chọn người nhận.

## AI đọc đồng hồ và thông báo (AiStack, cập nhật 2026-11-27)

Sơ đồ final, luồng AI và email. Code: `RoomLedger.Functions` (ReadDocument, MeterResult, Notify), `RoomLedger.Infrastructure` (BedrockAiClient, SsmConfigProvider, SesNotifier), `RoomLedger.Ai` (prompt, schema).

| Phần | Cách hoạt động |
|---|---|
| Kích hoạt | Ảnh tải lên bucket `incoming` → EventBridge → state machine `roomledger-{stage}-read-meter` (Standard). Rule này nhận mọi ảnh của bucket; bước đầu bỏ qua ảnh không phải ảnh đồng hồ |
| `meter-prepare` (VPC) | Tìm `meter_reading` PENDING_AI có `photo_key` đúng ảnh này; đổi `photo_status` PENDING_UPLOAD → READY. Chưa có Lambda nào làm việc này trước đây |
| `read-document` (**ngoài VPC**) | Thu ảnh về cạnh dài 1568 px (bỏ EXIF/GPS), gọi Bedrock Converse với Claude Haiku 4.5 qua inference profile `au.*`, kết quả JSON `{readable, value, confidence, note}`. Chỉ có quyền đọc ảnh `incoming`, đọc 3 tham số SSM và `bedrock:InvokeModel` đúng profile Haiku 4.5 |
| `meter-persist` (VPC) | Ghi `ai_value`, `ai_confidence`, `status`, `review_reason` bằng `MeterReadingService.Assess` (BR-20). Bedrock lỗi sau 2 lần thử lại hay ảnh không đọc được thành NEEDS_REVIEW (`AI_FAILED`) để chủ nhà nhập tay. NEEDS_REVIEW phát sự kiện `MeterReadingNeedsReview` cho chủ nhà |
| Parameter Store (Standard) | `/roomledger/{stage}/ai/model-id` và `/ai/prompt/readElectricMeter`, `/ai/prompt/readWaterMeter`. Nạp từ file ở `src/backend/src/RoomLedger.Ai/Prompts` mỗi lần deploy (sửa tay trên console thì lần deploy sau ghi đè). Sửa có hiệu lực sau tối đa 5 phút. Đổi sang họ mô hình khác phải sửa cả policy (`AiStack.BedrockProfilePrefix`) |
| `notify` (**ngoài VPC**) | Sự kiện trong bucket `outbox` → Lambda → SES. Mẫu có sẵn: InviteCreated, TenancyMoved, InvoiceSent, InvoiceDisputed, MeterReadingNeedsReview. Người nhận lấy từ `email`/`tenantEmail` của sự kiện, hoặc tra Cognito theo `notifyUserId`/`tenantId`/`landlordId`. Lỗi thử lại 2 lần rồi vào SQS `roomledger-{stage}-ai-dlq` (có báo động) |

**Cần làm trước khi chạy thật**
1. **Bedrock:** tài khoản phải gửi form use-case Anthropic và được dùng Claude Haiku 4.5 (Bedrock → Model access). Profile `au.*` định tuyến giữa `ap-southeast-2` và `ap-southeast-4`: SCP chặn `ap-southeast-4` thì Converse bị từ chối, khi đó phải nhờ mentor mở region hoặc đổi sang profile khác (đổi `-c aiModelId=...` và `BedrockProfilePrefix`).
2. **SES sandbox:** tài khoản SES mới chỉ gửi được tới địa chỉ đã xác minh. Stack **không** tạo identity và không ghi địa chỉ email nào. Xác minh người gửi rồi từng người nhận thử:
   ```
   aws sesv2 create-email-identity --email-identity <địa chỉ người gửi> --region ap-southeast-2
   aws sesv2 create-email-identity --email-identity <địa chỉ người nhận thử> --region ap-southeast-2
   ```
   Mỗi địa chỉ nhận một email xác minh, bấm liên kết trong đó. Gửi cho người lạ cần xin rời sandbox (SES → Account dashboard → Request production access).
3. **Deploy:** `cdk deploy --all -c stage=dev -c sesSender=<địa chỉ người gửi đã xác minh> -c webOrigin=<origin web>`. Không truyền `sesSender` thì stack vẫn dựng phần AI nhưng không dựng Lambda `notify` (truyền `sesSender` mà thiếu `webOrigin` thì synth báo lỗi).
4. Dashboard của ObservabilityStack đã có 4 Lambda này; chưa có báo động lỗi riêng cho chúng (báo động lỗi hiện có là `AlarmedLambdas`).

**Chi phí tăng thêm (ước lượng, chưa đo):** Step Functions Standard 3 đến 4 chuyển trạng thái mỗi ảnh tải lên (mọi ảnh, không chỉ ảnh đồng hồ); Bedrock Haiku 4.5 một lần gọi mỗi ảnh đồng hồ (ảnh ~1568 px); Parameter Store Standard, SES và SQS gần như 0 ở quy mô vài chục người dùng. Dọn dẹp: `cdk destroy` xóa toàn bộ (log group có hạn giữ, tham số SSM bị xóa theo stack).

## Giám sát (ObservabilityStack, Sang, cập nhật 2026-11-17)

Nhánh `feature/observability-stack` (dựa trên MD-06, mở PR sau khi MD-06 được merge).

| Thành phần | Chi tiết |
|---|---|
| Topic SNS `roomledger-{stage}-alarms` | Mọi báo động gửi vào đây, kể cả hai báo động của WorkflowStack. **Chưa có người nhận**: Phát chọn email rồi thêm subscription |
| Dashboard `roomledger-{stage}` | API (4xx/5xx, độ trễ), lỗi và throttle của từng Lambda, RDS (CPU, kết nối, dung lượng trống), DLQ |
| Báo động mới | `api-5xx` (≥ 5 lỗi/5 phút), `lambda-throttles` (bất kỳ throttle nào trong tài khoản), `rds-free-storage` (< 2 GB) |
| Hạn giữ log | Lambda của WorkflowStack: 30 ngày (dev, demo), 90 ngày (prod). Lambda của Api/Auth/Data (Tuấn) chưa đặt |

Tổng cộng 5 báo động và 1 dashboard: nằm trong free tier của CloudWatch. Chỉ số lấy theo tên `roomledger-{stage}-…`, không phụ thuộc output của stack khác (trừ topic, được WorkflowStack dùng).
