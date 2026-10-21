<#
.SYNOPSIS
    Smoke test end-to-end cho backend Restaurant Food Manager — chạy trên API thật (SQL Server).

.DESCRIPTION
    Script gọi HTTP vào API đang chạy (mặc định http://localhost:5225/api) và đi hết một ca
    vận hành thực tế của quán:

      0. Khởi tạo & đăng nhập
      1. Danh mục / món / cỡ / topping
      2. Kho: nguyên liệu, nhập kho, phiếu mua
      3. Công thức (có phiên bản)
      4. Bàn + khu vực
      5. Cấu hình quán (VAT + phí dịch vụ)
      6. Khuyến mãi (có target món)
      7. POS tạo đơn  → trừ kho + tạo phiếu bếp
      8. Bếp chạy phiếu (Preparing → Ready)
      9. Gọi thêm món (vòng bếp mới)
     10. Bảng bếp: ?since= (chỉ phiếu thay đổi)
     11. Áp khuyến mãi (chỉ giảm trong phạm vi target)
     12. Hoá đơn gộp theo bàn
     13. Ghi chú hoá đơn
     14. Chuyển bàn
     15. Thanh toán tiền mặt (kèm chặn thu trùng)
     16. Hoàn tiền
     17. Thanh toán MoMo (chế độ giả lập) + IPN giả
     18. Báo cáo doanh thu + CSV
     19. Tồn kho thấp + ledger
     20. Lịch sử đơn + dashboard

    Mọi dữ liệu tạo ra đều có tiền tố "E2E" để dễ nhận ra.

    LƯU Ý: file này phải lưu ở dạng **UTF-8 có BOM**, nếu không PowerShell 5.1 sẽ đọc
    sai tiếng Việt và báo lỗi parse. (Đã lưu sẵn BOM trong repo.)

.PARAMETER BaseUrl
    Địa chỉ API (không có dấu / cuối).

.PARAMETER UserName
    Tài khoản dùng cho ca chạy. Nếu chưa có sẽ tự đăng ký.

.PARAMETER Password
    Mật khẩu của tài khoản trên.

.EXAMPLE
    # 1) Chạy API
    dotnet run --project RestaurantFoodManagerSystem
    # 2) Chạy smoke test
    powershell -ExecutionPolicy Bypass -File scripts\e2e-smoke.ps1
#>
param(
    [string]$BaseUrl = "http://localhost:5225/api",
    [string]$UserName = "e2e-runner",
    [string]$Password = "E2E@123456"
)

$ErrorActionPreference = "Stop"

# Hiển thị tiếng Việt đúng trong console Windows (PowerShell 5.1 mặc định dùng OEM code page).
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch { }

$script:Pass = 0
$script:Fail = 0
$script:Fails = @()
$script:Token = $null

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "== $Text" -ForegroundColor Cyan
}

function Write-Ok([string]$Text) {
    $script:Pass++
    Write-Host "  [OK]   $Text" -ForegroundColor Green
}

function Write-Bad([string]$Text, [string]$Detail = "") {
    $script:Fail++
    $script:Fails += $Text
    Write-Host "  [FAIL] $Text" -ForegroundColor Red
    if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkRed }
}

function Write-Skip([string]$Text) {
    Write-Host "  [SKIP] $Text" -ForegroundColor DarkGray
}

# Gọi API. Trả về @{ Status; Body; Json; Ok }
function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        $Body = $null,
        [int[]]$Expect = @(200)
    )

    $params = @{
        Uri             = "$BaseUrl$Path"
        Method          = $Method
        Headers         = @{}
        TimeoutSec      = 60
        ErrorAction     = "Stop"
        UseBasicParsing = $true   # Bắt buộc với PowerShell 5.1 (không dùng engine IE)
    }
    if ($script:Token) { $params.Headers["Authorization"] = "Bearer $script:Token" }

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 8 -Compress
        # PHẢI gửi byte UTF-8: PowerShell 5.1 mặc định encode string body theo Latin-1,
        # làm hỏng tiếng Việt trong JSON và server trả 400.
        $params.Body = [System.Text.Encoding]::UTF8.GetBytes($json)
        $params.ContentType = "application/json; charset=utf-8"
    }

    $status = 0
    $text = ""
    try {
        $resp = Invoke-WebRequest @params
        $status = [int]$resp.StatusCode
        $text = $resp.Content
    } catch {
        if ($_.Exception.Response) {
            try { $status = [int]$_.Exception.Response.StatusCode } catch { $status = 0 }
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $text = $reader.ReadToEnd()
            } catch { }
        } else {
            $text = $_.Exception.Message
        }
    }

    $json = $null
    if ($text) {
        try { $json = $text | ConvertFrom-Json } catch { $json = $null }
    }

    return @{ Status = $status; Body = $text; Json = $json; Ok = ($Expect -contains $status) }
}

# Gọi API + tự đánh dấu OK/FAIL
function Check {
    param(
        [string]$Label,
        [string]$Method,
        [string]$Path,
        $Body = $null,
        [int[]]$Expect = @(200)
    )

    $r = Invoke-Api -Method $Method -Path $Path -Body $Body -Expect $Expect
    if ($r.Ok) {
        Write-Ok "$Label  [$($Method) $Path → $($r.Status)]"
    } else {
        Write-Bad "$Label  [$($Method) $Path → $($r.Status) (cần $($Expect -join '/'))]" $r.Body
    }
    return $r
}

function Assert-True([bool]$Condition, [string]$Label, [string]$Detail = "") {
    if ($Condition) { Write-Ok $Label } else { Write-Bad $Label $Detail }
}

# ----------------------------------------------------------------------
# 0. Khởi tạo & đăng nhập
# ----------------------------------------------------------------------
Write-Step "0. Khởi tạo & đăng nhập"

$health = Invoke-Api -Method GET -Path "/setup/status" -Expect @(200)
if ($health.Ok) {
    Write-Ok "API sống · /setup/status → requiresSetup=$($health.Json.requiresSetup)"
}
else {
    Write-Bad "Không gọi được API tại $BaseUrl" "hãy chạy: dotnet run --project RestaurantFoodManagerSystem"
    Write-Host ""
    Write-Host "KẾT QUẢ: 0 OK / 1 FAIL — dừng vì API không chạy." -ForegroundColor Red
    exit 1
}

$reg = Invoke-Api -Method POST -Path "/auth/register" -Expect @(200, 400, 409) -Body @{
    userName       = $UserName
    email          = "$UserName@example.test"
    password       = $Password
    confirmPassword = $Password
    fullName       = "E2E Runner"
    phone          = "0900000000"
}
if ($reg.Status -eq 200) { Write-Ok "Đăng ký tài khoản '$UserName'" }
else { Write-Host "  [--]   Tài khoản '$UserName' đã tồn tại (bỏ qua đăng ký)" -ForegroundColor DarkGray }

$login = Invoke-Api -Method POST -Path "/auth/login" -Expect @(200) -Body @{
    userName = $UserName
    password = $Password
}
if ($login.Ok -and $login.Json.data.token) {
    $script:Token = $login.Json.data.token
    Write-Ok "Đăng nhập thành công (roles: $(($login.Json.data.roles) -join ', '))"
}
else {
    Write-Bad "Đăng nhập thất bại" $login.Body
}

$badLogin = Invoke-Api -Method POST -Path "/auth/login" -Expect @(401) -Body @{
    userName = $UserName
    password = "sai-mat-khau"
}
if ($badLogin.Status -eq 401) { Write-Ok "Sai mật khẩu → 401 đúng" }
else { Write-Bad "Sai mật khẩu phải trả 401" "thực tế: $($badLogin.Status)" }

# ----------------------------------------------------------------------
# 1. Danh mục / món / cỡ / topping
# ----------------------------------------------------------------------
Write-Step "1. Danh mục · món · cỡ · topping"

$tag = (Get-Date).ToString("HHmmss")

$cat = Check "Tạo danh mục 'Món chính E2E'" -Method POST -Path "/categories" -Body @{
    name = "Món chính E2E $tag"; description = "Danh mục smoke test"; displayOrder = 1
}
# POST /categories trả { message } (không có id) ⇒ phải tra danh sách để lấy id.
$catList = Invoke-Api -Method GET -Path "/categories" -Expect @(200)
$categoryId = (@($catList.Json | Where-Object { $_.name -eq "Món chính E2E $tag" })[0]).id
Assert-True ($null -ne $categoryId) "Lấy được id danh mục sau khi tạo: $categoryId"

$cat2 = Check "Tạo danh mục 'Món phụ E2E'" -Method POST -Path "/categories" -Body @{
    name = "Món phụ E2E $tag"; description = "Danh mục phụ"; displayOrder = 2
}
$catList2 = Invoke-Api -Method GET -Path "/categories" -Expect @(200)
$category2Id = (@($catList2.Json | Where-Object { $_.name -eq "Món phụ E2E $tag" })[0]).id
Assert-True ($null -ne $category2Id) "Lấy được id danh mục phụ: $category2Id"

$food = Check "Tạo món 'Phở bò E2E' (60.000)" -Method POST -Path "/foods" -Expect @(200, 201) -Body @{
    categoryId = $categoryId; foodName = "Phở bò E2E $tag"; foodDescription = "Món chính"
    foodImageUrl = ""; foodPrice = 60000; isAvailable = $true
}
$foodId = $food.Json.foodId

$size = Check "Tạo cỡ 'L' (60.000, mặc định)" -Method POST -Path "/foods/$foodId/sizes" -Expect @(200, 201) -Body @{
    sizeName = "L"; sizePrice = 60000; isDefault = $true
}
$sizeId = $size.Json.sizeId

$food2 = Check "Tạo món 'Nước ngọt E2E' (20.000)" -Method POST -Path "/foods" -Expect @(200, 201) -Body @{
    categoryId = $category2Id; foodName = "Nước ngọt E2E $tag"; foodDescription = "Món phụ"
    foodImageUrl = ""; foodPrice = 20000; isAvailable = $true
}
$food2Id = $food2.Json.foodId

$size2 = Check "Tạo cỡ 'M' cho món phụ (20.000)" -Method POST -Path "/foods/$food2Id/sizes" -Expect @(200, 201) -Body @{
    sizeName = "M"; sizePrice = 20000; isDefault = $true
}
$size2Id = $size2.Json.sizeId

$topping = Check "Tạo topping 'Trứng lòng đào' (5.000)" -Method POST -Path "/toppings" -Expect @(200, 201) -Body @{
    toppingName = "Trứng lòng đào E2E $tag"; toppingPrice = 5000; isAvailable = $true
}
$toppingId = $topping.Json.toppingId

if ($foodId) {
    Check "Gắn topping vào món chính" -Method POST -Path "/foods/$foodId/toppings" -Body @{ toppingId = $toppingId } | Out-Null
}

# ----------------------------------------------------------------------
# 2. Kho
# ----------------------------------------------------------------------
Write-Step "2. Kho: nguyên liệu · nhập kho · phiếu mua"

$ing = Check "Tạo nguyên liệu 'Thịt bò E2E' (kg, tối thiểu 2)" -Method POST -Path "/management/ingredients" -Body @{
    name = "Thịt bò E2E $tag"; unit = "kg"; minimumStock = 2
}
$ingredientId = $ing.Json.ingredientId

$ing2 = Check "Tạo nguyên liệu 'Bột mì E2E' (kg, tối thiểu 5)" -Method POST -Path "/management/ingredients" -Body @{
    name = "Bột mì E2E $tag"; unit = "kg"; minimumStock = 5
}
$ingredient2Id = $ing2.Json.ingredientId

if ($ingredientId) {
    Check "Nhập kho thịt bò +10kg" -Method POST -Path "/management/inventory/$ingredientId/adjust" -Expect @(200, 204) -Body @{
        quantity = 10; createdBy = 1; note = "E2E nhập kho đầu"
    } | Out-Null
}
if ($ingredient2Id) {
    Check "Nhập kho bột mì +20kg" -Method POST -Path "/management/inventory/$ingredient2Id/adjust" -Expect @(200, 204) -Body @{
        quantity = 20; createdBy = 1; note = "E2E nhập kho đầu"
    } | Out-Null
}

$supplier = Check "Tạo nhà cung cấp" -Method POST -Path "/management/suppliers" -Body @{
    name = "Cty Thực Phẩm E2E $tag"; phone = "0900111222"
    email = "supplier-$tag@example.test"; address = "123 Đường Lê Lợi"
}
$supplierId = $supplier.Json.supplierId

if ($supplierId -and $ingredientId -and $ingredient2Id) {
    $po = Check "Tạo phiếu mua (2 dòng)" -Method POST -Path "/management/purchase-orders" -Body @{
        supplierId = $supplierId; createdBy = 1
        items = @(
            @{ ingredientId = $ingredientId;  quantity = 5; unitPrice = 180000 }
            @{ ingredientId = $ingredient2Id; quantity = 10; unitPrice = 12000 }
        )
    }
    $poId = $po.Json.id

    if ($poId) {
        Check "Nhận phiếu mua (cộng kho + ghi ledger Purchase)" -Method POST -Path "/management/purchase-orders/$poId/receive" -Expect @(200, 204) -Body @{ receivedBy = 1 } | Out-Null
        $recv2 = Invoke-Api -Method POST -Path "/management/purchase-orders/$poId/receive" -Body @{ receivedBy = 1 } -Expect @(404)
        if ($recv2.Status -eq 404) { Write-Ok "Nhận 2 lần → lần 2 bị chặn (404)" }
        else { Write-Bad "Nhận phiếu 2 lần phải bị chặn" "thực tế: $($recv2.Status)" }
    }
}

# ----------------------------------------------------------------------
# 3. Công thức
# ----------------------------------------------------------------------
Write-Step "3. Công thức (có phiên bản)"

if ($sizeId -and $ingredientId -and $ingredient2Id) {
    $recipe = Check "Tạo công thức cho cỡ L (thịt bò 0.2kg + bột mì 0.1kg)" -Method POST -Path "/management/recipes" -Body @{
        sizeId = $sizeId; name = "Công thức Phở E2E $tag"; version = "1"
        ingredients = @(
            @{ ingredientId = $ingredientId;  quantity = 0.2 }
            @{ ingredientId = $ingredient2Id; quantity = 0.1 }
        )
    }
    $recipeId = $recipe.Json.recipeId
}
if ($size2Id -and $ingredient2Id) {
    Check "Tạo công thức cho cỡ M của món phụ (bột mì 0.05kg)" -Method POST -Path "/management/recipes" -Body @{
        sizeId = $size2Id; name = "Công thức Nước ngọt E2E $tag"; version = "1"
        ingredients = @(@{ ingredientId = $ingredient2Id; quantity = 0.05 })
    } | Out-Null
}

if ($recipeId) {
    Check "Sửa công thức (tạo phiên bản 2, version tự tăng)" -Method PUT -Path "/management/recipes/$recipeId" -Expect @(200, 204) -Body @{
        sizeId = $sizeId; name = "Công thức Phở E2E $tag (v2)"; version = "1"
        ingredients = @(
            @{ ingredientId = $ingredientId;  quantity = 0.25 }
            @{ ingredientId = $ingredient2Id; quantity = 0.1 }
        )
    } | Out-Null

    $allRecipes = Invoke-Api -Method GET -Path "/management/recipes?includeInactive=true" -Expect @(200)
    $versions = @($allRecipes.Json | Where-Object { $_.sizeId -eq $sizeId })
    Assert-True ($versions.Count -eq 2) "Bản cũ vẫn còn (lịch sử): $((($versions | ForEach-Object { $_.version }) -join ', '))" "số bản: $($versions.Count)"
    # Phải bọc @() — `.Count` trên 1 object đơn trong PowerShell 5.1 trả $null.
    $activeVersions = @($versions | Where-Object { $_.isActive })
    Assert-True ($activeVersions.Count -eq 1) "Chỉ 1 bản đang hoạt động (version $($activeVersions[0].version))" "số bản active: $($activeVersions.Count)"
}

# ----------------------------------------------------------------------
# 4. Bàn + khu vực
# ----------------------------------------------------------------------
Write-Step "4. Khu vực + bàn"

$area = Check "Tạo khu vực 'Khu E2E'" -Method POST -Path "/table-areas" -Body @{
    areaName = "Khu E2E $tag"; description = "Khu vực smoke test"; displayOrder = 99
}
$areaId = $area.Json.areaId

$table = Check "Tạo bàn 'E2E-1' (4 chỗ)" -Method POST -Path "/restaurant-tables" -Body @{
    tableNumber = "E2E-1-$tag"; capacity = 4; areaId = $areaId
}
$tableId = $table.Json.data.tableId

$table2 = Check "Tạo bàn 'E2E-2' (2 chỗ)" -Method POST -Path "/restaurant-tables" -Body @{
    tableNumber = "E2E-2-$tag"; capacity = 2; areaId = $areaId
}
$table2Id = $table2.Json.data.tableId

# ----------------------------------------------------------------------
# 5. Cấu hình quán
# ----------------------------------------------------------------------
Write-Step "5. Cấu hình quán (VAT 10% + phí dịch vụ 5%)"

# C-22: cấu hình quán là dữ liệu DÙNG CHUNG của quán (singleton) — E2E đổi VAT/phí DV
# thì đổi luôn tiền thật của quán, và tên quán thành "Quán E2E Test".
# ⇒ Lưu cấu hình cũ trước, và KHÔI PHỤC lại ở cuối script (xem phần "Khôi phục cấu hình quán").
$settingsBackup = Invoke-Api -Method GET -Path "/management/settings" -Expect @(200)
$script:SettingsBackup = $settingsBackup.Json

Check "PUT /api/management/settings" -Method PUT -Path "/management/settings" -Body @{
    restaurantName = "Quán E2E Test"; address = "1 Đường Test"; phone = "0900123456"
    taxCode = "0101234567"; currency = "VND"
    taxRate = 10; taxIncluded = $true; serviceChargeRate = 5
} | Out-Null
$getSettings = Invoke-Api -Method GET -Path "/management/settings" -Expect @(200)
Assert-True ($getSettings.Json.taxRate -eq 10 -and $getSettings.Json.serviceChargeRate -eq 5) `
    "Đọc lại cấu hình: VAT $($getSettings.Json.taxRate)%, phí DV $($getSettings.Json.serviceChargeRate)%"

# ----------------------------------------------------------------------
# 6. Khuyến mãi
# ----------------------------------------------------------------------
Write-Step "6. Khuyến mãi (có target món)"

$promo = Check "Tạo khuyến mãi 10% (giờ VN)" -Method POST -Path "/management/promotions" -Body @{
    name = "Giảm 10% món chính E2E $tag"; description = "Chỉ áp cho món chính"
    discountType = "Percent"; discountValue = 10
    startAt = (Get-Date).AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss")
    endAt = (Get-Date).AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss")
    minimumOrderAmount = 0; maxDiscountAmount = 0
}
$promoId = $promo.Json.id

if ($promoId -and $sizeId) {
    Check "Gắn target: chỉ cỡ L của món chính" -Method PUT -Path "/management/promotions/$promoId/targets" -Expect @(200, 204) -Body @{
        sizeIds = @($sizeId); categoryIds = @()
    } | Out-Null

    $promoGet = Invoke-Api -Method GET -Path "/management/promotions/$promoId" -Expect @(200)
    if ($promoGet.Json) {
        $promoSizeIds = @($promoGet.Json.sizeIds)
        Assert-True ($promoSizeIds.Count -eq 1 -and $promoSizeIds[0] -eq $sizeId) `
            "Đọc lại target: sizeIds = $($promoGet.Json.sizeIds -join ',')"
    }

    $badTarget = Invoke-Api -Method PUT -Path "/management/promotions/$promoId/targets" -Body @{
        sizeIds = @(999999); categoryIds = @()
    } -Expect @(404)
    if ($badTarget.Status -eq 404) { Write-Ok "Target không tồn tại → 404 đúng" }
    else { Write-Bad "Target không tồn tại phải trả 404" "thực tế: $($badTarget.Status)" }
}

# ----------------------------------------------------------------------
# 7. POS tạo đơn
# ----------------------------------------------------------------------
Write-Step "7. POS tạo đơn (2 món, 1 món có topping)"

$stockBefore = Invoke-Api -Method GET -Path "/management/inventory" -Expect @(200)
$beefBefore = ($stockBefore.Json | Where-Object { $_.ingredientId -eq $ingredientId }).quanlity

$order = Check "POST /api/orders (Phở x2 có topping + Nước ngọt x1)" -Method POST -Path "/orders" -Body @{
    tableId = $tableId; createdBy = 1
    note = "E2E — xuất hoá đơn công ty ABC"
    items = @(
        @{ sizeId = $sizeId; quantity = 2; note = "ít hành"
           toppings = @(@{ toppingId = $toppingId; quantity = 1 }) }
        @{ sizeId = $size2Id; quantity = 1; note = "" }
    )
}
$orderId = $order.Json.orderId

$stockAfter = Invoke-Api -Method GET -Path "/management/inventory" -Expect @(200)
$beefAfter = ($stockAfter.Json | Where-Object { $_.ingredientId -eq $ingredientId }).quanlity
Assert-True ([math]::Abs(($beefBefore - $beefAfter) - 0.5) -lt 0.001) `
    "Tồn thịt bò trừ đúng 0.5kg (2 × 0.25 theo công thức v2): $beefBefore → $beefAfter" `
    "thực tế trừ $($beefBefore - $beefAfter)"

$detail = Invoke-Api -Method GET -Path "/orders/$orderId" -Expect @(200)
if ($detail.Json) {
    Assert-True ($detail.Json.status -eq "SentToKitchen") "Trạng thái đơn sau khi gọi: $($detail.Json.status)"
    Assert-True ($detail.Json.note -eq "E2E — xuất hoá đơn công ty ABC") "Ghi chú hoá đơn lưu đúng"
    # 2 × (60.000 size + 5.000 topping) + 1 × 20.000 = 150.000
    Assert-True ($detail.Json.subtotal -eq 150000) "Tiền hàng = 2×65.000 + 20.000 = $($detail.Json.subtotal)"
    # Phí dịch vụ 5% trên 150.000 = 7.500 (VAT đã gồm trong giá menu)
    Assert-True ($detail.Json.totalAmount -eq 157500) "Tổng = 150.000 + 5% phí DV 7.500 = $($detail.Json.totalAmount)"
}

# ----------------------------------------------------------------------
# 8. Bếp chạy phiếu
# ----------------------------------------------------------------------
Write-Step "8. Bếp chạy phiếu (Preparing → Ready)"

$openTickets = Invoke-Api -Method GET -Path "/management/kitchen/orders?status=open" -Expect @(200)
$myTicket = @($openTickets.Json | Where-Object { $_.orderId -eq $orderId })[0]
if ($myTicket) {
    Assert-True ($null -ne $myTicket) "Phiếu bếp đã tạo (kitchenOrderId=$($myTicket.kitchenOrderId))"
    Assert-True ($myTicket.tableNumber -eq "E2E-1-$tag") "Phiếu bếp có số bàn: $($myTicket.tableNumber)"
    Assert-True ($myTicket.itemCount -eq 2) "Phiếu bếp có số món: $($myTicket.itemCount)"

    Check "Bếp nhận phiếu (Preparing)" -Method PATCH -Path "/management/kitchen/orders/$($myTicket.kitchenOrderId)/status" -Expect @(200, 204) -Body @{
        status = "Preparing"; changedBy = 1
    } | Out-Null

    $ticketItems = Invoke-Api -Method GET -Path "/management/kitchen/orders/$($myTicket.kitchenOrderId)/items" -Expect @(200)
    foreach ($it in $ticketItems.Json) {
        Check "Món '$($it.foodName)' → Ready" -Method PATCH -Path "/management/kitchen/items/$($it.id)/status" -Expect @(200, 204) -Body @{
            status = "Ready"; changedBy = 1
        } | Out-Null
    }

    $orderAfterKitchen = Invoke-Api -Method GET -Path "/orders/$orderId" -Expect @(200)
    Assert-True ($orderAfterKitchen.Json.status -eq "Ready") "Đơn chuyển sang Ready sau khi bếp xong: $($orderAfterKitchen.Json.status)"
}
else {
    Write-Skip "Không có phiếu bếp cho đơn này — bỏ qua bước 8"
}

# ----------------------------------------------------------------------
# 9. Gọi thêm món (vòng bếp mới)
# ----------------------------------------------------------------------
Write-Step "9. Gọi thêm món (vòng bếp mới) + bếp nấy xong vòng 2"

if ($orderId) {
    $add = Check "POST /api/orders/$orderId/items (gọi thêm 1 Phở)" -Method POST -Path "/orders/$orderId/items" -Body @{
        changedBy = 1; items = @(@{ sizeId = $sizeId; quantity = 1; note = "nhanh" })
    }
    if ($add.Json) {
        # 150.000 + 60.000 (gọi thêm KHÔNG kèm topping) = 210.000
        Assert-True ($add.Json.subtotal -eq 210000) "Tiền hàng sau gọi thêm = 210.000: $($add.Json.subtotal)"
        Assert-True ($add.Json.status -eq "SentToKitchen") "Đơn quay lại SentToKitchen (vòng bếp mới): $($add.Json.status)"
    }

    $tickets2 = Invoke-Api -Method GET -Path "/management/kitchen/orders?status=open" -Expect @(200)
    $myTickets = @($tickets2.Json | Where-Object { $_.orderId -eq $orderId })
    Assert-True ($myTickets.Count -eq 2) "Đã có 2 phiếu bếp cho 2 vòng: $($myTickets.Count)"

    # Bếp phải nấy xong VÒNG 2 thì thu ngân mới thu được tiền (luật P1e).
    $round2 = @($myTickets | Where-Object { $_.status -eq "Pending" })[0]
    if ($round2) {
        Check "Bếp nhận phiếu vòng 2 (Preparing)" -Method PATCH -Path "/management/kitchen/orders/$($round2.kitchenOrderId)/status" -Expect @(200, 204) -Body @{
            status = "Preparing"; changedBy = 1
        } | Out-Null

        $items2 = Invoke-Api -Method GET -Path "/management/kitchen/orders/$($round2.kitchenOrderId)/items" -Expect @(200)
        foreach ($it in $items2.Json) {
            Check "Món vòng 2 '$($it.foodName)' → Ready" -Method PATCH -Path "/management/kitchen/items/$($it.id)/status" -Expect @(200, 204) -Body @{
                status = "Ready"; changedBy = 1
            } | Out-Null
        }
    }

    $orderAfterRound2 = Invoke-Api -Method GET -Path "/orders/$orderId" -Expect @(200)
    if ($orderAfterRound2.Json) {
        Assert-True ($orderAfterRound2.Json.status -eq "Ready") "Đơn Ready sau khi bếp xong vòng 2: $($orderAfterRound2.Json.status)"
    }
}

# ----------------------------------------------------------------------
# 10. Bảng bếp ?since=
# ----------------------------------------------------------------------
Write-Step "10. Bảng bếp: ?since= (chỉ phiếu thay đổi)"

$allTickets = Invoke-Api -Method GET -Path "/management/kitchen/orders" -Expect @(200)
$myAll = @($allTickets.Json | Where-Object { $_.orderId -eq $orderId })
Assert-True ($myAll.Count -eq 2) "Không lọc → thấy cả 2 phiếu: $($myAll.Count)"

$oldSince = (Get-Date).AddHours(-1).ToString("o")
$sinceOld = Invoke-Api -Method GET -Path "/management/kitchen/orders?status=open&since=$([uri]::EscapeDataString($oldSince))" -Expect @(200)
Assert-True (@($sinceOld.Json).Count -ge 2) "since=1 giờ trước → thấy cả 2 phiếu: $(@($sinceOld.Json).Count)"

$newSince = (Get-Date).ToString("o")
$sinceNew = Invoke-Api -Method GET -Path "/management/kitchen/orders?status=open&since=$([uri]::EscapeDataString($newSince))" -Expect @(200)
Assert-True (@($sinceNew.Json).Count -eq 0) "since=hiện tại → không phiếu nào thay đổi: $(@($sinceNew.Json).Count)"

$page = Invoke-Api -Method GET -Path "/management/kitchen/orders?page=1&pageSize=1" -Expect @(200)
Assert-True (@($page.Json).Count -eq 1) "Phân trang pageSize=1 → trả 1 phiếu: $(@($page.Json).Count)"

# ----------------------------------------------------------------------
# 11. Áp khuyến mãi
# ----------------------------------------------------------------------
Write-Step "11. Áp khuyến mãi (chỉ giảm trong phạm vi target)"

if ($orderId -and $promoId) {
    Check "PATCH /api/orders/$orderId/promotion/$promoId" -Method PATCH -Path "/orders/$orderId/promotion/$promoId" -Expect @(200, 204) -Body @{} | Out-Null
    $afterPromo = Invoke-Api -Method GET -Path "/orders/$orderId" -Expect @(200)
    if ($afterPromo.Json) {
        # Target = cỡ L ⇒ base = 2×65.000 (có topping) + 1×60.000 (gọi thêm) = 190.000
        # Món "Nước ngọt" (cỡ M) KHÔNG nằm trong target ⇒ không được giảm.
        Assert-True ($afterPromo.Json.discountAmount -eq 19000) `
            "Giảm đúng phạm vi target (190.000 × 10% = 19.000, món ngoài target không bị giảm): $($afterPromo.Json.discountAmount)"
        Assert-True ($afterPromo.Json.promotionId -eq $promoId) "Đơn lưu promotionId + snapshot"
    }
}

# ----------------------------------------------------------------------
# 12. Hoá đơn gộp theo bàn
# ----------------------------------------------------------------------
Write-Step "12. Hoá đơn gộp theo bàn"

if ($tableId) {
    $bill = Check "GET /api/orders/table/$tableId/bill" -Method GET -Path "/orders/table/$tableId/bill"
    if ($bill.Json) {
        Assert-True ($bill.Json.orderCount -ge 1) "Số đơn trong hoá đơn: $($bill.Json.orderCount)"
        Assert-True (@($bill.Json.items).Count -ge 1) "Số dòng món (đã gộp): $(@($bill.Json.items).Count)"
        Assert-True (@($bill.Json.notes).Count -ge 1) "Ghi chú hoá đơn: $($bill.Json.notes -join ' | ')"
    }
}
if ($table2Id) {
    $billMissing = Invoke-Api -Method GET -Path "/orders/table/$table2Id/bill" -Expect @(404)
    if ($billMissing.Status -eq 404) { Write-Ok "Bàn không có đơn mở → 404 đúng" }
    else { Write-Bad "Bàn trống phải trả 404" "thực tế: $($billMissing.Status)" }
}

# ----------------------------------------------------------------------
# 13. Ghi chú hoá đơn
# ----------------------------------------------------------------------
Write-Step "13. Ghi chú hoá đơn (B3)"

if ($orderId) {
    Check "PATCH /api/orders/$orderId/note" -Method PATCH -Path "/orders/$orderId/note" -Expect @(200, 204) -Body @{
        note = "E2E — khách đợi bạn, giữ món sau"; changedBy = 1
    } | Out-Null
    $noteGet = Invoke-Api -Method GET -Path "/orders/$orderId" -Expect @(200)
    if ($noteGet.Json) {
        Assert-True ($noteGet.Json.note -eq "E2E — khách đợi bạn, giữ món sau") "Ghi chú cập nhật: $($noteGet.Json.note)"
    }

    $histAfterNote = Invoke-Api -Method GET -Path "/orders/$orderId/history" -Expect @(200)
    if ($histAfterNote.Json) {
        $billNoteRows = @($histAfterNote.Json | Where-Object { $_.note -like "*bill note*" })
        Assert-True ($billNoteRows.Count -ge 1) "Lịch sử đơn có dòng ghi chú"
    }

    Check "Xoá ghi chú (gửi chuỗi rỗng)" -Method PATCH -Path "/orders/$orderId/note" -Expect @(200, 204) -Body @{ note = ""; changedBy = 1 } | Out-Null
    $noteCleared = Invoke-Api -Method GET -Path "/orders/$orderId" -Expect @(200)
    if ($noteCleared.Json) {
        Assert-True ($null -eq $noteCleared.Json.note) "Ghi chú đã xoá"
    }
}

# ----------------------------------------------------------------------
# 14. Chuyển bàn
# ----------------------------------------------------------------------
Write-Step "14. Chuyển bàn (B1)"

if ($orderId -and $tableId -and $table2Id) {
    Check "PATCH /api/orders/$orderId/table → bàn E2E-2" -Method PATCH -Path "/orders/$orderId/table" -Expect @(200, 204) -Body @{
        tableId = $table2Id; changedBy = 1
    } | Out-Null
    $tablesAfterMove = Invoke-Api -Method GET -Path "/restaurant-tables" -Expect @(200)
    $t1 = @($tablesAfterMove.Json | Where-Object { $_.tableId -eq $tableId })[0]
    $t2 = @($tablesAfterMove.Json | Where-Object { $_.tableId -eq $table2Id })[0]
    if ($t1) { Assert-True ($t1.status -eq "Available") "Bàn cũ → Available: $($t1.status)" }
    if ($t2) { Assert-True ($t2.status -eq "Occupied") "Bàn mới → Occupied: $($t2.status)" }

    Check "Chuyển bàn về lại bàn cũ" -Method PATCH -Path "/orders/$orderId/table" -Expect @(200, 204) -Body @{
        tableId = $tableId; changedBy = 1
    } | Out-Null
}

# ----------------------------------------------------------------------
# 15. Thanh toán tiền mặt
# ----------------------------------------------------------------------
Write-Step "15. Thanh toán tiền mặt"

if ($orderId) {
    $pay = Check "POST /api/payments (tiền mặt, có mã giao dịch)" -Method POST -Path "/payments" -Body @{
        orderId = $orderId; paymentMethod = "Cash"
        transactionCode = "E2E-TXN-$tag"; createdBy = 1
    }
    $paymentId = $pay.Json.paymentId
    if ($paymentId) {
        Assert-True ($pay.Json.transactionCode -eq "E2E-TXN-$tag") "Mã giao dịch giữ nguyên: $($pay.Json.transactionCode)"
        Assert-True ($pay.Json.amount -gt 0) "Tiền thu = tổng đơn: $($pay.Json.amount)"
    }

    $paid = Invoke-Api -Method GET -Path "/orders/$orderId" -Expect @(200)
    if ($paid.Json) { Assert-True ($paid.Json.status -eq "Paid") "Đơn → Paid: $($paid.Json.status)" }

    $dupPay = Invoke-Api -Method POST -Path "/payments" -Body @{
        orderId = $orderId; paymentMethod = "Cash"; createdBy = 1
    } -Expect @(409)
    if ($dupPay.Status -eq 409) { Write-Ok "Thu trùng đơn đã trả → 409 đúng" }
    else { Write-Bad "Thu trùng phải trả 409" "thực tế: $($dupPay.Status)" }

    $tablesPaid = Invoke-Api -Method GET -Path "/restaurant-tables" -Expect @(200)
    $t1Paid = @($tablesPaid.Json | Where-Object { $_.tableId -eq $tableId })[0]
    if ($t1Paid) { Assert-True ($t1Paid.status -eq "Available") "Bàn trả tiền xong → Available: $($t1Paid.status)" }

    # Doanh thu phải có tiền NGAY khi đã thu (trước khi hoàn).
    $revNow = Invoke-Api -Method GET -Path "/reports/revenue?period=day" -Expect @(200)
    if ($revNow.Json) {
        Assert-True ([decimal]$revNow.Json.totalRevenue -ge $pay.Json.amount) `
            "Doanh thu hôm nay đã ghi nhận khoản vừa thu: $($revNow.Json.totalRevenue)"
    }
}

# ----------------------------------------------------------------------
# 16. Hoàn tiền
# ----------------------------------------------------------------------
Write-Step "16. Hoàn tiền"

if ($paymentId) {
    $refund = Check "POST /api/payments/$paymentId/refund" -Method POST -Path "/payments/$paymentId/refund" -Body @{
        reason = "E2E — thu nhầm"; createdBy = 1
    }
    if ($refund.Json) { Assert-True ($refund.Json.status -eq "Refunded") "Giao dịch → Refunded: $($refund.Json.status)" }

    $afterRefund = Invoke-Api -Method GET -Path "/orders/$orderId" -Expect @(200)
    if ($afterRefund.Json) { Assert-True ($afterRefund.Json.status -eq "Ready") "Đơn mở lại Ready: $($afterRefund.Json.status)" }
}

# ----------------------------------------------------------------------
# 17. MoMo (chế độ giả lập) + IPN giả
# ----------------------------------------------------------------------
Write-Step "17. Thanh toán MoMo (chế độ giả lập — không gọi ra ngoài)"

if ($orderId) {
    $momo = Check "POST /api/payments/momo (tạo intent)" -Method POST -Path "/payments/momo" -Body @{
        orderId = $orderId; createdBy = 1
    }
    if ($momo.Json) {
        $momoPaymentId = $momo.Json.paymentId
        Assert-True ($momo.Json.status -eq "Pending") "Intent MoMo ở trạng thái: $($momo.Json.status)"
        Assert-True ($momo.Json.isMock -eq $true) "Chế độ giả lập: isMock=$($momo.Json.isMock)"

        # IPN với chữ ký sai ⇒ phải bị từ chối
        $ipnBad = Invoke-Api -Method POST -Path "/payments/momo/notify" -Body @{
            partnerCode = "MOCK_PARTNER"; partnerRefCode = "$orderId"; amount = "1000"
            orderId = "$orderId"; requestId = $momo.Json.requestId; requestType = "capture"
            resultCode = "9000"; responseCode = "9000"; extraData = "1"; message = "ok"
            transId = "MOCK-E2E-1"; signature = "0123456789abcdef0123456789abcdef"
        } -Expect @(200)
        if ($ipnBad.Json) {
            Assert-True ($ipnBad.Json.accepted -eq $false) "IPN chữ ký sai bị từ chối (accepted=$($ipnBad.Json.accepted))"
        }

        $orderAfterBadIpn = Invoke-Api -Method GET -Path "/orders/$orderId" -Expect @(200)
        if ($orderAfterBadIpn.Json) {
            Assert-True ($orderAfterBadIpn.Json.status -eq "Ready") "Đơn vẫn Ready sau IPN giả: $($orderAfterBadIpn.Json.status)"
        }
    }
}

# ----------------------------------------------------------------------
# 18. Báo cáo doanh thu + CSV
# ----------------------------------------------------------------------
Write-Step "18. Báo cáo doanh thu + xuất CSV"

$revenue = Check "GET /api/reports/revenue?period=day" -Method GET -Path "/reports/revenue?period=day"
if ($revenue.Json) {
    Assert-True ($null -ne $revenue.Json.totalRevenue) "Doanh thu hôm nay: $($revenue.Json.totalRevenue)"
    Assert-True ([decimal]$revenue.Json.totalRevenue -eq 0) `
        "Doanh thu = 0 sau khi đã hoàn tiền (đã thu rồi hoàn ⇒ không còn là doanh thu)" `
        "thực tế: $($revenue.Json.totalRevenue)"
}

$csv = Invoke-Api -Method GET -Path "/reports/revenue/export.csv?period=day" -Expect @(200)
Assert-True ($csv.Body -like "*TOTAL*") "CSV có dòng TOTAL"
$csvLines = @($csv.Body -split "`n" | Where-Object { $_.Trim() -ne "" })
Write-Host "  [--]   CSV ($($csvLines.Count) dòng):" -ForegroundColor DarkGray
$csvLines | Select-Object -First 3 | ForEach-Object { Write-Host "         $_" -ForegroundColor DarkGray }

# ----------------------------------------------------------------------
# 19. Tồn kho thấp + ledger
# ----------------------------------------------------------------------
Write-Step "19. Tồn kho thấp (B4) + ledger kho"

$lowStock = Check "GET /api/management/inventory/low-stock" -Method GET -Path "/management/inventory/low-stock"
if ($lowStock.Json -and $ingredientId) {
    $lowForThis = @($lowStock.Json | Where-Object { $_.ingredientId -eq $ingredientId })
    Assert-True ($lowForThis.Count -eq 1) "Nguyên liệu E2E nằm trong danh sách sắp hết"
    if ($lowForThis.Count -eq 1) {
        Assert-True ($lowForThis[0].minimumStock -eq 2) "Mức tối thiểu: $($lowForThis[0].minimumStock) $($lowForThis[0].unit)"
        Assert-True ($lowForThis[0].shortage -gt 0) "Còn thiếu: $($lowForThis[0].shortage)"
    }
}

if ($ingredientId) {
    $ledger = Invoke-Api -Method GET -Path "/management/inventory-transactions?ingredientId=$ingredientId&pageSize=10" -Expect @(200)
    if ($ledger.Json) {
        $types = (@($ledger.Json.items | ForEach-Object { $_.type }) | Select-Object -Unique) -join ", "
        Assert-True (@($ledger.Json.items | Where-Object { $_.unit -eq "kg" }).Count -ge 1) `
            "Dòng ledger có đơn vị (kg). Các loại giao dịch: $types"
    }
}

# ----------------------------------------------------------------------
# 20. Lịch sử đơn + dashboard
# ----------------------------------------------------------------------
Write-Step "20. Lịch sử đơn + dashboard"

if ($orderId) {
    $history = Invoke-Api -Method GET -Path "/orders/$orderId/history" -Expect @(200)
    if ($history.Json) {
        $notes = @($history.Json | ForEach-Object { $_.note })
        Assert-True (@($history.Json).Count -ge 4) "Lịch sử đơn có $(@($history.Json).Count) dòng: $($notes -join ' → ')"
    }
}

$dashboard = Check "GET /api/reports/dashboard" -Method GET -Path "/reports/dashboard"
if ($dashboard.Json -and $dashboard.Json.tables) {
    Assert-True ($null -ne $dashboard.Json.tables) `
        "Bàn: $(($dashboard.Json.tables | ForEach-Object { "$($_.total) tổng / $($_.occupied) có khách" }) -join ', ')"
}

# ----------------------------------------------------------------------
# Khôi phục cấu hình quán (C-22)
# ----------------------------------------------------------------------
# Bắt buộc: E2E đã đổi VAT/phí DV/tên quán để kiểm tra công thức tiền.
# Nếu không khôi phục thì quán thật bị để lại cấu hình "Quán E2E Test" (VAT 10%, phí DV 5%).
if ($script:SettingsBackup) {
    try {
        $b = $script:SettingsBackup
        Invoke-Api -Method PUT -Path "/management/settings" -Expect @(200) -Body @{
            restaurantName = $b.restaurantName
            address = $b.address
            phone = $b.phone
            taxCode = $b.taxCode
            currency = $b.currency
            taxRate = $b.taxRate
            taxIncluded = $b.taxIncluded
            serviceChargeRate = $b.serviceChargeRate
        } | Out-Null
        Write-Host "Đã khôi phục cấu hình quán (VAT $($b.taxRate)%, phí DV $($b.serviceChargeRate)%)." -ForegroundColor DarkGray
    } catch {
        Write-Host "CẢNH BÁO: không khôi phục được cấu hình quán — hãy tự PUT lại /api/management/settings" -ForegroundColor Yellow
    }
}

# ----------------------------------------------------------------------
# Tổng kết
# ----------------------------------------------------------------------
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
$color = if ($script:Fail -eq 0) { "Green" } else { "Red" }
Write-Host "KẾT QUẢ E2E: $($script:Pass) OK / $($script:Fail) FAIL" -ForegroundColor $color
if ($script:Fail -gt 0) {
    Write-Host "Các case FAIL:" -ForegroundColor Red
    $script:Fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}
Write-Host "Dữ liệu tạo ra đều có tiền tố 'E2E' trong tên." -ForegroundColor DarkGray
Write-Host "============================================================" -ForegroundColor Cyan

if ($script:Fail -gt 0) { exit 1 }
