# Village Bank Core

A small core banking system for village banks and microfinance: members, savings, loans, a double-entry
general ledger and the reports a manager needs. It is an ASP.NET Core 8 Web API on PostgreSQL.

It models how a village bank in rural Laos runs: a teller opens accounts and takes deposits, a manager
approves loans, an auditor reads the books, and every kip that moves is recorded in a ledger that always balances.

> The data in the demo is made up. Names are typical Lao names written in Latin letters and the phone
> numbers are obviously fake (`020 0000 00NN`).

## What it does

| Area | What you can do |
|---|---|
| **Members** | Register a member (a savings account is opened with them), search, list |
| **Savings** | Deposit, withdraw, statement. Safe against double clicks and simultaneous withdrawals |
| **Loans** | Preview a repayment schedule, apply, approve or reject, disburse, repay, early payoff |
| **Ledger** | Double-entry journal. Savings and loans post to it automatically; a manager can post manual entries |
| **Reports** | Trial balance, balance sheet, income statement, daily cash book, loan aging with PAR, reconciliation. All export to CSV |
| **Access** | JWT login, four roles, users created by an administrator, temporary passwords |

## Tech stack

C# / .NET 8, ASP.NET Core Web API, PostgreSQL 16, Entity Framework Core 8 (writes), Dapper (report SQL),
JWT bearer authentication, xUnit with Testcontainers, Docker Compose, GitHub Actions.

---

## Quick start (Docker)

You need Docker with the Compose plugin.

```bash
git clone git@github.com:kamarlaylatt/village-bank-core.git
cd village-bank-core
cp .env.example .env
```

Edit `.env` and fill in the empty values (see [Configuration](#configuration)). A strong signing key:

```bash
openssl rand -base64 48
```

Then start it:

```bash
docker compose up -d db
docker compose run --rm api migrate
docker compose up -d --build api
```

Check that it is alive:

```bash
curl http://localhost:8080/health
```

It answers `Healthy`. With `Swagger__Enabled=true` in `.env`, open <http://localhost:8080/swagger>, call
`POST /api/auth/login` with the administrator from `.env`, and paste the returned `accessToken` into the
**Authorize** button.

### Demo data (optional)

The database starts empty. To fill it with about 30 members, six months of savings and 12 loans in every
state (current, late, repaid, rejected), run this once. It is a separate command and never runs on its own:

```bash
docker compose run --rm api seed-demo
```

It needs `Seed__Demo__Password` in `.env`. It creates three staff users, `manager01`, `teller01` and
`auditor01`, who all use that password. It does nothing if the database already has members.

---

## Deploying

These are the commands, in order. `migrate` and `seed-demo` are separate steps on purpose: deploying never
changes data you did not ask it to change.

### First deploy

```bash
git clone git@github.com:kamarlaylatt/village-bank-core.git
cd village-bank-core
cp .env.example .env            # then fill it in; see Configuration
docker compose up -d db         # start PostgreSQL and wait until it is healthy
docker compose run --rm api migrate
docker compose up -d --build api
curl http://localhost:8080/health
```

The administrator in `.env` is created the first time the API starts, when no administrator exists yet.

### Releasing an update

```bash
git pull
docker compose build api
docker compose run --rm api migrate     # applies new migrations; does nothing if there are none
docker compose up -d api
curl http://localhost:8080/health
```

Migrations only go forward. Take a backup first (below) when a release contains a new migration.

### Everyday commands

```bash
docker compose ps                       # what is running
docker compose logs -f api              # follow the API log
docker compose restart api              # restart the same container; it does NOT re-read .env (see below)
docker compose down                     # stop everything; the data volume is kept
```

### Backup and restore

```bash
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' > backup-$(date +%F).sql
```

Restore into an empty database (after a reset, below):

```bash
docker compose exec -T db sh -c 'psql -U "$POSTGRES_USER" "$POSTGRES_DB"' < backup-2026-10-30.sql
```

### Reset the database (like `migrate:fresh`)

This **deletes all data**. Use it on a laptop or a demo server, never on production.

Docker, keeping the PostgreSQL container and its volume:

```bash
docker compose exec db sh -c 'dropdb -U "$POSTGRES_USER" --force "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
docker compose run --rm api migrate
docker compose restart api              # recreates the administrator from .env
docker compose run --rm api seed-demo   # optional
```

Docker, wiping everything including the volume:

```bash
docker compose down -v
docker compose up -d db
docker compose run --rm api migrate
docker compose up -d api
```

Without Docker for the app (the database still runs from compose):

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef database drop --force --project src/VillageBank.Api
dotnet run --project src/VillageBank.Api -- migrate
dotnet run --project src/VillageBank.Api -- seed-demo    # optional
```

### Before going live

- Put the API behind a reverse proxy that terminates HTTPS. The container speaks plain HTTP on 8080.
- Remove the `ports` entry of the `db` service in `docker-compose.yml`; only the API needs the database.
- Set `Swagger__Enabled=false`.
- Use long random values for `POSTGRES_PASSWORD`, `Jwt__SigningKey` and the administrator password.
- Set `CORS_ORIGIN` to the real address of the web front end.
- Do not run `seed-demo`.

---

## Configuration

All settings are environment variables. Docker Compose reads them from `.env`.

| Variable | Required | Meaning |
|---|---|---|
| `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` | yes | The database and its login |
| `Jwt__SigningKey` | yes | Secret that signs login tokens. At least 32 characters; the API refuses to start otherwise |
| `Seed__SuperAdmin__Username` | yes | Name of the first administrator |
| `Seed__SuperAdmin__Password` | yes | Password of the first administrator, at least 8 characters |
| `Seed__Demo__Password` | for `seed-demo` | Shared password of the demo staff users |
| `Swagger__Enabled` | no | `true` serves Swagger UI. Default `false` |
| `CORS_ORIGIN` | no | Browser origin allowed to call the API. Default `http://localhost:3000` |

`.env` is ignored by git. Only `.env.example`, which holds placeholders, is committed.

### After changing `.env`

Docker reads `.env` only when it creates a container, so `docker compose restart api` keeps the old values.
Recreate the container instead:

```bash
docker compose up -d api
```

Compose recreates `api` only if its settings changed. What each change means:

| Changed value | What to do and what happens |
|---|---|
| `Swagger__Enabled`, `CORS_ORIGIN` | `docker compose up -d api` |
| `Jwt__SigningKey` | `docker compose up -d api`. Every login token stops working, so everyone signs in again |
| `Seed__SuperAdmin__Username`, `Seed__SuperAdmin__Password` | Used only while no administrator exists. Changing them later does nothing; change a password through the API |
| `Seed__Demo__Password` | Used only by `seed-demo`. Existing demo users keep their old password |
| `POSTGRES_PASSWORD` | PostgreSQL stores the password in its data volume when the volume is first created, so editing `.env` alone locks the API out. Change it in the database first (below), then edit `.env`, then `docker compose up -d api` |
| `POSTGRES_USER`, `POSTGRES_DB` | Fixed when the volume is created. Changing them needs a [reset](#reset-the-database-like-migratefresh) |

Changing the database password:

```bash
docker compose exec -it db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

Inside psql run `\password villagebank` (use your `POSTGRES_USER`), type the new password twice, then `\q`.
Put the same password in `.env` and run `docker compose up -d api`.

Running the API with `dotnet run` instead: .NET does not read `.env`. Copy the new values into user-secrets
(see [Local development](#local-development-without-docker-for-the-app)) and restart `dotnet run`:

```bash
bash scripts/sync-secrets.sh
```

Code changes need a rebuild, not just a recreate: `docker compose up -d --build api`.

## Commands

The app has two one-off commands. They read the same configuration as the API but do not start the web server.

| Command | What it does |
|---|---|
| `migrate` | Applies pending database migrations. Safe to repeat |
| `seed-demo` | Creates the demo data on an empty database |

```bash
docker compose run --rm api migrate
dotnet run --project src/VillageBank.Api -- migrate     # without Docker
```

## Local development without Docker for the app

The database still comes from compose (`docker compose up -d db`). .NET does not read `.env`; it reads
user-secrets, which live outside the project folder and never reach git. One command copies everything the API
needs from `.env`:

```bash
bash scripts/sync-secrets.sh
```

It sets the connection string (built from `POSTGRES_*`), `Jwt:SigningKey`, the administrator and the demo
password, and it never prints a value. Run it again whenever `.env` changes. Then:

```bash
dotnet run --project src/VillageBank.Api -- migrate
dotnet run --project src/VillageBank.Api
```

To set a single secret by hand, reading it from `.env`:

```bash
dotnet user-secrets set "Jwt:SigningKey" "$(grep '^Jwt__SigningKey=' .env | cut -d= -f2-)" --project src/VillageBank.Api
```

To inspect or remove secrets:

```bash
dotnet user-secrets list --project src/VillageBank.Api | cut -d= -f1    # names only, values stay hidden
dotnet user-secrets remove "Jwt:SigningKey" --project src/VillageBank.Api
dotnet user-secrets clear --project src/VillageBank.Api
```

Swagger is on in the Development environment, at <http://localhost:5272/swagger>.

## Tests

```bash
dotnet test
```

Docker must be running: the integration tests start PostgreSQL through Testcontainers. There are 142 tests,
most of them against a real database. They include concurrency tests (twenty simultaneous withdrawals never
overdraw an account), a replay of a whole village story checked against exact ledger balances, and a test that
builds the schema from the migrations on an empty database. CI runs them on every push.

---

## Roles

| Role | Can do |
|---|---|
| **SuperAdmin** | Create, deactivate and change the role of users. Cannot see or touch any financial data |
| **Manager** | Everything a teller does, plus approve or reject loans, post manual journal entries, read all reports |
| **Teller** | Register members, deposit and withdraw, apply for loans, disburse and take repayments, read the cash book |
| **Auditor** | Read only: members, savings, loans and every report |

Keeping user administration apart from money handling is deliberate (separation of duties).

## API overview

| Area | Endpoints |
|---|---|
| Auth | `POST /api/auth/login`, `POST /api/auth/change-password`, `GET /api/auth/me` |
| Users | `GET` and `POST /api/users`, `PUT /api/users/{id}/role`, `POST /api/users/{id}/deactivate`, `activate`, `reset-password` |
| Members | `POST` and `GET /api/members`, `GET /api/members/{id}` |
| Savings | `GET /api/savings-accounts/{id}`, `.../statement`, `POST .../deposits`, `POST .../withdrawals` |
| Loans | `POST /api/loans/preview`, `POST` and `GET /api/loans`, `GET /api/loans/{id}`, `POST .../approve`, `reject`, `disburse`, `repayments` |
| Ledger | `POST /api/journal-entries` |
| Reports | `GET /api/reports/trial-balance`, `balance-sheet`, `income-statement`, `cash-book`, `loan-aging`, `reconciliation` (add `?format=csv`) |
| Health | `GET /health` (no login) |

Every endpoint needs a login except `/api/auth/login` and `/health`; a new endpoint is protected unless it opts out.

---

## How it works

### The ledger and the subledgers

Every movement of money is a journal entry of two or more lines whose debits equal its credits. Three
layers guard that rule: a validator, the service that saves the entry, and database constraints. Entries are
never edited or deleted.

The ledger holds totals per account (Cash, Member Savings, Loans Receivable, Interest Income). Who owns
what lives in the subledgers: `savings_accounts` and `savings_transactions` for savings, `loans`,
`loan_installments` and `loan_repayments` for loans. Each subledger row points at its journal entry, and
both are saved in one database transaction. The reconciliation report checks that the subledgers add up to the ledger.

| Event | Journal entry |
|---|---|
| Deposit | Dr Cash, Cr Member Savings |
| Withdrawal | Dr Member Savings, Cr Cash |
| Loan disbursed (paid into the member's savings) | Dr Loans Receivable, Cr Member Savings |
| Loan repayment | Dr Cash; Cr Loans Receivable for the principal; Cr Interest Income for the interest |

### Money

Amounts are `decimal` in `numeric(19,4)` columns, never floating point. Kip has no decimals, so loan amounts
are whole numbers and the rounding remainder goes to the last installment.

### Loans

Interest is a flat rate: charged every month on the original principal. A loan goes from `Pending` to
`Approved` to `Active` and finally `Closed`, or ends as `Rejected`. The repayment schedule is created at
disbursement because it starts from that date. A repayment pays the oldest installment first, interest before
principal. A member can have one loan in progress at a time, enforced by a partial unique index as well as the code.

The person who applied for a loan cannot approve it (maker-checker).

### Safe under concurrency and retries

A deposit, withdrawal, loan change or repayment locks the row it changes (`SELECT ... FOR UPDATE`), so
simultaneous requests wait their turn and cannot overdraw an account. Money endpoints accept an
`Idempotency-Key` header: a retry with the same key returns the original result instead of posting twice,
which matters on a poor rural connection.

### Reports

The report queries are plain SQL run through Dapper. Loan aging gives each active loan its days past due
and groups loans into Current, 1-30, 31-60, 61-90 and 90+ days. PAR(n) is the principal of loans more than n
days late as a share of the whole portfolio. CSV export neutralises cells that start with `=`, `+`, `-` or `@`
(spreadsheet formula injection) and writes UTF-8 with a byte order mark so Excel reads Lao and Burmese names.

### Security

Passwords are stored as PBKDF2 hashes. A login token is checked against the database on every request, so
deactivating a user or changing their role takes effect at once. A user with a temporary password can do
nothing but change it. Unknown users and wrong passwords give the same answer in the same time.

---

## Known limitations

- Interest is recognised as income when it is paid (cash basis), not as it accrues.
- No late fees or penalty interest yet.
- Loan aging shows today's position only. It cannot be run for a past date, because the schedule keeps what has been paid but not when each installment was settled.
- No login rate limiting, token revocation or audit-log table. Who did what is kept in `created_by_user_id` columns and the application log.
- Demo journal entries are dated in the past, but their `created_at` is the moment the demo was seeded.

## Roadmap: AI loan assessment

The aim is a decision-support score, not an automatic decision. A manager sees a recommendation (approve,
review, decline), a suggested maximum amount and the main reasons, then decides. Overriding the
recommendation requires a written reason.

- **Explainable**: a logistic regression or scorecard whose weights can be read, so a decline can be explained to the member.
- **Inputs**: past loans and how on time they were repaid, savings balance and regularity, membership age, requested amount against savings. These already exist in the data; the installments need a "settled on" date.
- **Fairness**: gender, ethnicity, religion and village are not inputs.
- **Auditable**: each assessment stores the model version, the inputs and the reasons.
- **Honest data**: with no real history yet the model would be trained on the synthetic demo data, so it would show the pipeline, not real accuracy.

## Project layout

```
src/VillageBank.Api
  Auth/          JWT setup, roles, password policy, first-administrator seeder
  Cli/           the migrate and seed-demo commands
  Contracts/     request and response shapes
  Controllers/   HTTP endpoints
  Data/          DbContext and migrations
  Demo/          demo data seeder and its simulated clock
  Domain/        entities and business rules (schedule calculator, validators)
  Middleware/    error handling
  Services/      the business logic: ledger, savings, loans, reports, CSV
tests/VillageBank.Tests
scripts/sync-secrets.sh    copies .env into user-secrets for dotnet run
Dockerfile, docker-compose.yml, .github/workflows/ci.yml
```
