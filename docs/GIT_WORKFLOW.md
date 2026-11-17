# NijMitra — Git Workflow

* Remote: `https://github.com/sachin4443/nijmitra.com`. Feature work for this build happened on `claude/upbeat-brown-qo4i4j`; merge into `main` via a reviewed pull request.
* Branches: `main` (production), `feature/<topic>`, `fix/<topic>`. Never force-push shared branches; never delete branches you didn't create.
* Commit style: Conventional Commits — `feat:`, `fix:`, `docs:`, `test:`, `chore:`, `refactor:`.

## Before every commit
1. `dotnet build` and `NIJMITRA_TEST_DB=… dotnet test NIJMITRA.Tests` (all green).
2. `cd NIJMITRA.Web && npm run build && npx vitest run`.
3. Review `git diff`; check for secrets: `git grep -nE "Password=|SigningKey\"\\s*:\\s*\"[^\"]|DataEncryptionKey\"\\s*:\\s*\"[^\"]|BEGIN (RSA|PRIVATE)"` must find nothing.
4. Update `docs/DEVELOPMENT_PROGRESS.md` (and other docs if behaviour changed).

## Never commit
`appsettings.Local.json`, `.env*` (except `.env.example`), `App_Data/` (dev keys, storage, encrypted DB connection), `logs/`, certificates (`*.pfx`, `*.pem`, `*.key`), `NIJMITRA.API/wwwroot/` (build output), `node_modules/`, `bin/`, `obj/`. These are in `.gitignore`.

## Database changes
Every schema change ships as an EF Core migration in the same commit as the entity change. Never edit an applied migration — add a new one.
