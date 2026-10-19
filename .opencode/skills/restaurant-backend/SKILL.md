---
description: Backend-only engineering rules for the Restaurant Food
  Manager System. Use when inspecting, designing, implementing,
  reviewing, or testing this repository.
name: restaurant-backend
---

# Restaurant Food Manager System --- Backend Skill

## 1. Hard boundary: backend only

**This project is backend-only. Do not create, edit, refactor,
configure, or generate frontend code.**

Never work on: - HTML, CSS, JavaScript/TypeScript UI, React, Vue,
Angular, Blazor UI, Razor views/pages, frontend components, layouts,
styling, client-side routing, or browser behavior. - UI mockups,
screens, visual design, frontend assets, or frontend build
configuration. - Any existing frontend directory or frontend project,
even if a backend task appears to suggest changing it.

Allowed work: - ASP.NET Core Web API, controllers/endpoints, application
services/use cases, domain models and rules, DTOs, validation,
authentication/authorization, EF Core, database migrations, persistence,
background jobs, logging, tests, API documentation, CSV export, and
backend integrations. - If a request cannot be completed without
frontend changes, explain the backend portion that can be done and ask
before considering any scope change. Do not silently touch the frontend.

Before editing, inspect the repository structure and identify the
backend project(s). Keep changes limited to the files required for the
requested backend task.

## 2. Product definition and boundaries

This is an **internal, single-restaurant/one-branch management system**.
The restaurant owns and operates the system. It is a closed internal
workflow; do not add customer-facing accounts, public self-registration,
delivery-platform integration, supplier portals, or other external
business integrations unless the owner explicitly changes the scope.

The staff roles are: - **Admin**: highest privilege; manages accounts,
role assignments, system configuration, and has authorized access to all
operational functions. - **Service staff**: creates and updates orders
within permitted workflow rules; cannot manage payments, inventory,
recipes, users, or permissions unless explicitly granted. - **Cashier**:
handles bill review, payment recording, receipts/payment status, and
closing eligible orders; cannot edit recipes or manage stock. -
**Warehouse manager**: manages ingredients/materials, stock receipts and
adjustments, stock movements, suppliers/purchase records if included in
scope, and stock warnings; cannot alter payment records or grant
roles. - **Kitchen staff**: views and processes kitchen tickets, updates
preparation status, and marks items/orders ready; cannot edit prices,
payments, recipes, inventory quantities, users, or permissions.

Enforce permissions on the server/API, not merely in UI. Never trust a
role or user ID supplied by the client without validating it against the
authenticated principal and database.

## 3. Core business rules

### Authentication and authorization

-   Accounts are created by Admin; no public self-registration by
    default.
-   Store password hashes using a standard ASP.NET Core password-hashing
    approach; never store or log plaintext passwords.
-   Use authentication plus role/policy-based authorization on protected
    endpoints.
-   Validate role changes and account activation/deactivation. Prevent
    accidental privilege escalation and protect critical Admin
    operations from unauthorized users.
-   Use a transaction when creating an account and its initial role
    assignment.
-   Record relevant actor and timestamp information for sensitive
    changes.

### Food catalog and pricing

-   Support categories, foods, available sizes/variants, and optional
    toppings/add-ons only where the existing model requires them.
-   A sellable size/variant has its own price. Validate that the
    selected food and size are active and compatible.
-   When an order item is created, **snapshot the unit price** into the
    order item. Later menu price changes must not rewrite historical
    orders.
-   Do not hard-delete records that are referenced by historical orders
    or financial records; prefer deactivation/soft deletion where
    appropriate.
-   Validate quantity, price, identifiers, and relationships on the
    server.

### Recipes and ingredients

-   Recipes and ingredient quantities are **planning/reporting data**,
    not proof that the kitchen physically used the exact amounts.
-   Use recipe-to-ingredient mappings to estimate expected consumption
    and support stock/reporting calculations.
-   Do not claim the system can verify that the kitchen followed a
    recipe unless a separate real-world verification process is
    explicitly implemented.
-   Define units clearly and validate unit compatibility/conversions.
    Avoid silently mixing grams, kilograms, millilitres, litres, and
    pieces.
-   Recipe changes should not silently rewrite historical reporting. If
    historical accuracy matters, use recipe versions/effective dates or
    snapshot the relevant recipe data at order time.

### Inventory

-   Represent stock changes as auditable inventory
    transactions/movements (receipt, usage/estimated usage, adjustment,
    return, waste, or other explicitly supported reason), rather than
    changing quantities with no trace.
-   Track who performed a movement, when it occurred, quantity, unit,
    reason, and the related order/purchase/adjustment when applicable.
-   Prevent invalid quantities and define whether negative stock is
    prohibited. Default to rejecting a transaction that would take stock
    below zero unless the business explicitly approves another policy.
-   Keep physical stock and recipe-based estimated consumption
    conceptually distinct. Reports must label estimates as estimates.
-   Provide low-stock thresholds/warnings; do not treat a warning as an
    automatic purchase order unless that workflow is implemented.
-   Use database transactions and concurrency-safe updates for stock
    movements to avoid double deductions or lost updates.

### Orders and kitchen workflow

-   Support multiple orders for a table when the domain allows it; do
    not assume a table can have only one active order unless the
    established requirements say so.
-   Validate the table, food, size/variant, add-ons, quantities,
    availability, and price on the server when creating an order.
-   Keep order status transitions explicit and validated. Do not allow
    arbitrary status jumps from client input.
-   Maintain order/status history with actor and timestamp for important
    transitions.
-   Create kitchen tickets from accepted orders or order items according
    to the current domain model. Kitchen staff update preparation
    status; service staff can see readiness and serve the order.
-   Consider item-level status when an order can contain dishes with
    different preparation times.
-   Avoid duplicating kitchen tickets or stock movements when requests
    are retried. Use idempotency or state checks where duplicate
    execution would cause harm.
-   Do not automatically deduct real stock at order creation unless that
    is the chosen policy. Document and consistently implement the chosen
    point, such as when the order is accepted, prepared, or closed. If
    using recipe-based deduction, describe it as estimated usage.

### Payments and closing

-   Payment belongs to the cashier workflow and must be validated
    server-side.
-   Define whether one bill can combine multiple eligible orders for a
    table and whether split payments, partial payments, refunds, or
    discounts are in scope. Do not invent these workflows without
    checking the existing requirements.
-   Snapshot amounts used for payment; calculate totals on the server,
    never trust client-submitted totals.
-   Prevent paying/closing the same bill twice. Use transactions and
    idempotency/state checks.
-   Keep a traceable record of payment method, amount, time, cashier,
    and status where relevant. Do not store payment-card secrets.
-   Discounts/promotions should only be implemented if included in the
    current project scope; validate them on the server and preserve the
    applied discount details in historical records.

### Reports and CSV export

-   Provide backend reports for operational data and revenue.
-   Export revenue to CSV for downstream Python-agent analysis. Treat
    the CSV as a data contract: stable column names, UTF-8 encoding
    (prefer UTF-8 with BOM if required by spreadsheet compatibility),
    invariant numeric/date formats, documented timezone, and correct CSV
    escaping.
-   Distinguish gross sales, discounts, refunds, net revenue, and
    payment totals if those concepts exist in the schema. Do not label
    order value as collected revenue unless payment status supports it.
-   Include a reporting period and clearly define whether the report is
    based on order time, payment time, or order-closing time.
-   Exclude unpaid/cancelled orders from collected revenue, or represent
    them separately with explicit status columns.
-   Keep the CSV export read-only and authorized. Do not send data to
    external services by default.
-   The Python agent or an in-process .NET agent is **not yet a settled
    architecture decision**. Keep report generation independent from
    agent implementation; propose options and trade-offs before adding
    an agent, provider, API key, or external dependency.

## 4. Recommended backend architecture

Follow the architecture already established in the repository. Prefer a
pragmatic Clean Architecture separation:

-   **API/Presentation**: HTTP endpoints, authentication wiring,
    request/response DTOs, API-level concerns.
-   **Application**: use cases, orchestration, validation,
    authorization-relevant business workflows, interfaces for
    external/persistence concerns when needed.
-   **Domain**: entities, value objects, enums, and core business
    invariants.
-   **Infrastructure**: EF Core `DbContext`, mappings, migrations,
    persistence implementations, file/CSV generation adapters, and
    external-provider adapters if explicitly approved.

Rules: - Inspect the existing solution before proposing a new
architecture. Do not perform a large rewrite for a small feature. -
Prefer use-case/service abstractions for meaningful business workflows
(orders, inventory, kitchen, payments). Avoid creating an interface and
repository class for every entity merely to wrap `DbContext`. - Simple
CRUD may use the existing `DbContext`/application service approach if
consistent with the codebase. - Do not put business logic in controllers
or repositories. Controllers should remain thin. - Keep DTOs at
API/application boundaries; do not expose EF entities directly as public
API contracts. - Use dependency injection and configuration. Do not
hard-code connection strings, secrets, model providers, API keys, or
environment-specific paths. - Use EF Core migrations for schema changes.
Never silently drop data or make destructive migrations without
explaining the impact. - Use async APIs and cancellation tokens where
appropriate. - Avoid introducing CQRS, MediatR, event buses,
microservices, or extra layers unless the task justifies the complexity.

## 5. Data integrity, security, and reliability

-   Add database constraints/indexes for important uniqueness and
    relationship rules where appropriate.
-   Use transactions for multi-step operations that must succeed or fail
    together (order creation, account plus role creation, payment
    closing, stock movement).
-   Validate all inputs on the server and return consistent API errors
    without exposing stack traces or secrets.
-   Apply least privilege, protect sensitive endpoints, and avoid
    mass-assignment vulnerabilities.
-   Log operational events usefully, but never log passwords, tokens,
    API keys, or sensitive secrets.
-   Handle concurrency and retries for payments, order creation, and
    inventory updates.
-   Preserve historical records required for reports and audit trails.
-   Do not claim that recipe-based inventory or reports represent
    physically verified kitchen behavior.

## 6. Testing expectations

For each meaningful backend change: - Add or update unit tests for
business rules and invalid state transitions. - Add integration tests
for database-backed workflows where practical. - Test authorization for
both allowed and denied roles. - Test duplicate/retry behavior for
payments, order submission, and stock movements. - Test CSV headers,
escaping, encoding, date boundaries, totals, and inclusion/exclusion
rules. - Do not change frontend tests or add frontend tooling.

## 7. How to work on a task

1.  Read the relevant code, entities, configuration, and existing tests
    before editing.
2.  Summarize the current behavior and the smallest safe change.
3.  Identify assumptions or unresolved business rules rather than
    inventing them.
4.  Implement only the requested backend scope.
5.  Run relevant backend tests/build if available; report commands and
    results honestly.
6.  Summarize changed files, migrations, API behavior, validation, and
    any remaining decisions.
7.  Explicitly state that no frontend files were changed.

## 8. Scope decisions to preserve unless the owner changes them

-   One restaurant/one branch.
-   Internal staff system only; no customer-facing portal or public
    registration.
-   Roles: Admin, Service staff, Cashier, Warehouse manager, Kitchen
    staff.
-   Recipes/ingredients provide estimated operational and reporting
    data; they do not prove actual kitchen compliance.
-   Revenue CSV export is a planned handoff to analysis. The agent
    runtime/provider is undecided.
-   Backend only. **Frontend work is prohibited.**
