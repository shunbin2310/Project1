param(
    [string]$BaseUrl = "http://localhost:5165",
    [string]$DemoPassword = "Project1Demo123!"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Invoke-ProjectApi {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("GET", "POST", "PUT", "DELETE")]
        [string]$Method,

        [Parameter(Mandatory)]
        [string]$Path,

        [string]$Token,
        [object]$Body
    )

    $headers = @{}
    if ($Token) {
        $headers.Authorization = "Bearer $Token"
    }

    $parameters = @{
        Uri         = "$BaseUrl$Path"
        Method      = $Method
        Headers     = $headers
        ErrorAction = "Stop"
    }

    if ($null -ne $Body) {
        $parameters.ContentType = "application/json"
        $parameters.Body = $Body | ConvertTo-Json -Depth 12
    }

    try {
        $response = Invoke-RestMethod @parameters

        # Invoke-RestMethod can preserve a JSON array as one pipeline object when it is
        # returned from another function. Explicitly enumerate arrays so callers using
        # @(...).Count receive the number of API records instead of always receiving 1.
        if ($response -is [System.Array]) {
            return $response | ForEach-Object { $_ }
        }

        return $response
    }
    catch {
        $detail = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($detail)) {
            $detail = $_.Exception.Message
        }
        throw "$Method $Path failed: $detail"
    }
}

function Login-DemoUser {
    param([Parameter(Mandatory)][string]$Email)

    $login = Invoke-ProjectApi -Method POST -Path "/api/auth/login" -Body @{
        email    = $Email
        password = $DemoPassword
    }

    if (-not $login.accessToken) {
        throw "Login did not return an access token for $Email."
    }

    return $login.accessToken
}

function Assert-Equal {
    param(
        [object]$Actual,
        [object]$Expected,
        [Parameter(Mandatory)][string]$Message
    )

    if ([string]$Actual -ne [string]$Expected) {
        throw "$Message Expected '$Expected', but received '$Actual'."
    }

    Write-Host "PASS: $Message" -ForegroundColor Green
}

function Assert-True {
    param(
        [bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }

    Write-Host "PASS: $Message" -ForegroundColor Green
}

function Assert-DecimalEqual {
    param(
        [decimal]$Actual,
        [decimal]$Expected,
        [Parameter(Mandatory)][string]$Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', but received '$Actual'."
    }

    Write-Host "PASS: $Message" -ForegroundColor Green
}

Write-Step "Checking API health"
$health = Invoke-ProjectApi -Method GET -Path "/api/health"
Write-Host "API health response: $($health.status)"

Write-Step "Signing in with all four demo roles"
$requesterToken = Login-DemoUser -Email "requester@demo.local"
$departmentToken = Login-DemoUser -Email "department@demo.local"
$financeToken = Login-DemoUser -Email "finance@demo.local"
$adminToken = Login-DemoUser -Email "admin@demo.local"

Write-Step "Checking the active Purchase Request workflow template"
$templates = @(Invoke-ProjectApi -Method GET -Path "/api/workflow-templates" -Token $adminToken)
$activeTemplate = $templates |
    Where-Object { $_.code -eq "PURCHASE_REQUEST" -and $_.isPublished -and $_.isActive } |
    Sort-Object version -Descending |
    Select-Object -First 1

Assert-True -Condition ($null -ne $activeTemplate) `
    -Message "An active, published PURCHASE_REQUEST workflow template is available."

$runId = Get-Date -Format "yyyyMMddHHmmssfff"
$today = (Get-Date).Date
$requiredDate = $today.AddDays(14).ToString("yyyy-MM-dd")
$validUntil = $today.AddDays(30).ToString("yyyy-MM-dd")
$expectedDeliveryDate = $today.AddDays(7).ToString("yyyy-MM-dd")
$businessDate = $today.ToString("yyyy-MM-dd")

Write-Step "Creating master data for run $runId"
$category = Invoke-ProjectApi -Method POST -Path "/api/product-categories" -Token $adminToken -Body @{
    name        = "E2E Office Equipment $runId"
    description = "Created by the complete system smoke test."
}

$unit = Invoke-ProjectApi -Method POST -Path "/api/units-of-measure" -Token $adminToken -Body @{
    code        = "E2E$($runId.Substring($runId.Length - 10))"
    name        = "E2E Unit $runId"
    description = "Unit used by the complete system smoke test."
}

$product = Invoke-ProjectApi -Method POST -Path "/api/products" -Token $adminToken -Body @{
    name                 = "Ergonomic Keyboard $runId"
    description          = "Inventory item created by the complete system smoke test."
    productCategoryId    = $category.id
    unitOfMeasureId      = $unit.id
    defaultUnitPrice     = 120.00
    reorderLevel         = 3
}

$supplierA = Invoke-ProjectApi -Method POST -Path "/api/suppliers" -Token $adminToken -Body @{
    name          = "Alpha Equipment $runId"
    contactPerson = "Alpha Sales"
    email         = "alpha.$runId@example.test"
    phone         = "0123456789"
    address       = "Kuala Lumpur"
}

$supplierB = Invoke-ProjectApi -Method POST -Path "/api/suppliers" -Token $adminToken -Body @{
    name          = "Beta Equipment $runId"
    contactPerson = "Beta Sales"
    email         = "beta.$runId@example.test"
    phone         = "0198765432"
    address       = "Selangor"
}

$null = Invoke-ProjectApi -Method POST -Path "/api/supplier-products" -Token $adminToken -Body @{
    supplierId = $supplierA.id
    productId  = $product.id
    isPreferred = $false
}

$null = Invoke-ProjectApi -Method POST -Path "/api/supplier-products" -Token $adminToken -Body @{
    supplierId = $supplierB.id
    productId  = $product.id
    isPreferred = $true
}

Write-Host "Created product $($product.code), supplier $($supplierA.code), and supplier $($supplierB.code)."

Write-Step "Creating and submitting a Purchase Request as Requester"
$purchaseRequest = Invoke-ProjectApi -Method POST -Path "/api/purchase-requests" -Token $requesterToken -Body @{
    requiredDate = $requiredDate
    justification = "Ten keyboards are required for the E2E onboarding test."
    items = @(
        @{
            productId = $product.id
            quantity  = 10
        }
    )
}

Assert-Equal -Actual $purchaseRequest.workflow.currentStepCode -Expected "DRAFT" `
    -Message "A new Purchase Request starts in Draft."

$purchaseRequest = Invoke-ProjectApi -Method POST `
    -Path "/api/purchase-requests/$($purchaseRequest.id)/actions/SUBMIT" `
    -Token $requesterToken `
    -Body @{ comment = "Submitted by the complete system smoke test." }

Assert-Equal -Actual $purchaseRequest.workflow.currentStepCode -Expected "DEPARTMENT_REVIEW" `
    -Message "Requester submission moves the request to Department Review."

Write-Step "Approving the request as Department Approver"
$purchaseRequest = Invoke-ProjectApi -Method POST `
    -Path "/api/purchase-requests/$($purchaseRequest.id)/actions/APPROVE" `
    -Token $departmentToken `
    -Body @{ comment = "Department budget owner approved." }

Assert-Equal -Actual $purchaseRequest.workflow.currentStepCode -Expected "FINANCE_REVIEW" `
    -Message "Department approval moves the request to Finance Review."

Write-Step "Approving the request as Finance Approver"
$purchaseRequest = Invoke-ProjectApi -Method POST `
    -Path "/api/purchase-requests/$($purchaseRequest.id)/actions/APPROVE" `
    -Token $financeToken `
    -Body @{ comment = "Finance confirmed the available budget." }

Assert-Equal -Actual $purchaseRequest.workflow.currentStepCode -Expected "APPROVED" `
    -Message "Finance approval completes the Purchase Request workflow."
Assert-Equal -Actual $purchaseRequest.workflow.status -Expected "Completed" `
    -Message "The approved workflow is completed."

$purchaseRequestItemId = $purchaseRequest.items[0].id

Write-Step "Recording and submitting two supplier quotations"
$quotationA = Invoke-ProjectApi -Method POST -Path "/api/quotations" -Token $adminToken -Body @{
    purchaseRequestId          = $purchaseRequest.id
    supplierId                = $supplierA.id
    supplierQuotationReference = "ALPHA-$runId"
    quotationDate             = $businessDate
    validUntil                = $validUntil
    notes                     = "Alpha quotation created by smoke test."
    items                     = @(
        @{
            purchaseRequestItemId = $purchaseRequestItemId
            unitPrice             = 115.00
        }
    )
}

$quotationB = Invoke-ProjectApi -Method POST -Path "/api/quotations" -Token $adminToken -Body @{
    purchaseRequestId          = $purchaseRequest.id
    supplierId                = $supplierB.id
    supplierQuotationReference = "BETA-$runId"
    quotationDate             = $businessDate
    validUntil                = $validUntil
    notes                     = "Beta quotation created by smoke test."
    items                     = @(
        @{
            purchaseRequestItemId = $purchaseRequestItemId
            unitPrice             = 108.00
        }
    )
}

$quotationA = Invoke-ProjectApi -Method POST -Path "/api/quotations/$($quotationA.id)/submit" -Token $adminToken
$quotationB = Invoke-ProjectApi -Method POST -Path "/api/quotations/$($quotationB.id)/submit" -Token $adminToken
Assert-Equal -Actual $quotationA.status -Expected "Submitted" -Message "Supplier A quotation is Submitted."
Assert-Equal -Actual $quotationB.status -Expected "Submitted" -Message "Supplier B quotation is Submitted."

$comparison = Invoke-ProjectApi -Method GET `
    -Path "/api/quotations/comparison?purchaseRequestId=$($purchaseRequest.id)" `
    -Token $adminToken
Assert-Equal -Actual @($comparison.quotations).Count -Expected 2 `
    -Message "Quotation comparison contains both suppliers."

$quotationB = Invoke-ProjectApi -Method POST -Path "/api/quotations/$($quotationB.id)/select" -Token $adminToken
$quotationA = Invoke-ProjectApi -Method GET -Path "/api/quotations/$($quotationA.id)" -Token $adminToken
Assert-Equal -Actual $quotationB.status -Expected "Selected" -Message "The lower Beta quotation is Selected."
Assert-Equal -Actual $quotationA.status -Expected "NotSelected" `
    -Message "The competing Alpha quotation becomes Not Selected."

Write-Step "Creating and issuing a Purchase Order from the selected quotation"
$purchaseOrder = Invoke-ProjectApi -Method POST -Path "/api/purchase-orders" -Token $adminToken -Body @{
    quotationId         = $quotationB.id
    orderDate           = $businessDate
    expectedDeliveryDate = $expectedDeliveryDate
    deliveryAddress     = "Project1 Main Warehouse"
    notes               = "Purchase order created by the complete system smoke test."
}
Assert-Equal -Actual $purchaseOrder.status -Expected "Draft" -Message "A new Purchase Order starts in Draft."

$purchaseOrder = Invoke-ProjectApi -Method POST `
    -Path "/api/purchase-orders/$($purchaseOrder.id)/issue" `
    -Token $adminToken
Assert-Equal -Actual $purchaseOrder.status -Expected "Issued" -Message "The Purchase Order is Issued."

$purchaseOrderItemId = $purchaseOrder.items[0].id
$initialInventory = Invoke-ProjectApi -Method GET -Path "/api/inventory/$($product.id)" -Token $adminToken
$initialQuantity = [decimal]$initialInventory.quantityOnHand

Write-Step "Creating the first partial Goods Receipt"
$receiptOne = Invoke-ProjectApi -Method POST -Path "/api/goods-receipts" -Token $adminToken -Body @{
    purchaseOrderId            = $purchaseOrder.id
    supplierDeliveryNoteNumber = "DN-1-$runId"
    receivedDate               = $businessDate
    notes                      = "First partial delivery."
    items                      = @(
        @{
            purchaseOrderItemId = $purchaseOrderItemId
            quantityReceived     = 6
        }
    )
}
Assert-Equal -Actual $receiptOne.status -Expected "Draft" -Message "A new Goods Receipt starts in Draft."

$inventoryBeforePost = Invoke-ProjectApi -Method GET -Path "/api/inventory/$($product.id)" -Token $adminToken
Assert-DecimalEqual -Actual ([decimal]$inventoryBeforePost.quantityOnHand) -Expected $initialQuantity `
    -Message "A Draft Goods Receipt does not change inventory."

$receiptOne = Invoke-ProjectApi -Method POST `
    -Path "/api/goods-receipts/$($receiptOne.id)/post" `
    -Token $adminToken
$purchaseOrder = Invoke-ProjectApi -Method GET -Path "/api/purchase-orders/$($purchaseOrder.id)" -Token $adminToken
$inventoryAfterFirstPost = Invoke-ProjectApi -Method GET -Path "/api/inventory/$($product.id)" -Token $adminToken

Assert-Equal -Actual $receiptOne.status -Expected "Posted" -Message "The first Goods Receipt is Posted."
Assert-Equal -Actual $purchaseOrder.status -Expected "PartiallyReceived" `
    -Message "Receiving 6 of 10 changes the Purchase Order to Partially Received."
Assert-DecimalEqual -Actual ([decimal]$inventoryAfterFirstPost.quantityOnHand) `
    -Expected ($initialQuantity + 6) `
    -Message "Posting the first receipt adds 6 units to inventory."

Write-Step "Receiving the remaining quantity"
$receiptTwo = Invoke-ProjectApi -Method POST -Path "/api/goods-receipts" -Token $adminToken -Body @{
    purchaseOrderId            = $purchaseOrder.id
    supplierDeliveryNoteNumber = "DN-2-$runId"
    receivedDate               = $businessDate
    notes                      = "Final delivery."
    items                      = @(
        @{
            purchaseOrderItemId = $purchaseOrderItemId
            quantityReceived     = 4
        }
    )
}
$receiptTwo = Invoke-ProjectApi -Method POST `
    -Path "/api/goods-receipts/$($receiptTwo.id)/post" `
    -Token $adminToken

$purchaseOrder = Invoke-ProjectApi -Method GET -Path "/api/purchase-orders/$($purchaseOrder.id)" -Token $adminToken
$finalInventory = Invoke-ProjectApi -Method GET -Path "/api/inventory/$($product.id)" -Token $adminToken
$transactions = @(Invoke-ProjectApi -Method GET `
    -Path "/api/inventory/$($product.id)/transactions?type=GoodsReceipt" `
    -Token $adminToken)

Assert-Equal -Actual $purchaseOrder.status -Expected "Received" `
    -Message "Receiving all 10 units completes the Purchase Order."
Assert-DecimalEqual -Actual ([decimal]$finalInventory.quantityOnHand) `
    -Expected ($initialQuantity + 10) `
    -Message "The final inventory balance increased by 10 units."
Assert-Equal -Actual $transactions.Count -Expected 2 `
    -Message "The inventory ledger contains two Goods Receipt movements."
Assert-DecimalEqual -Actual ([decimal](($transactions | Measure-Object -Property quantityChange -Sum).Sum)) `
    -Expected 10 `
    -Message "The two ledger movements add up to the received quantity."

Write-Host "`nComplete system smoke test passed." -ForegroundColor Green
Write-Host "Run ID: $runId"
Write-Host "Purchase Request: $($purchaseRequest.requestNumber)"
Write-Host "Selected Quotation: $($quotationB.quotationNumber)"
Write-Host "Purchase Order: $($purchaseOrder.purchaseOrderNumber)"
Write-Host "Goods Receipts: $($receiptOne.goodsReceiptNumber), $($receiptTwo.goodsReceiptNumber)"
Write-Host "Product: $($product.code)"
Write-Host "Final quantity on hand: $($finalInventory.quantityOnHand) $($finalInventory.unitOfMeasureCode)"
