#!/usr/bin/env bash
# T0 — .NET 8 toolchain check for the team layout (src/backend, src/infra).
# Run from anywhere:  bash scripts/verify-toolchain.sh
# Exit code 0 = every repo check passed. WARN lines are machine tools CI does not need.
set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

fails=0
pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1"; fails=$((fails + 1)); }
warn() { printf 'WARN  %s\n' "$1"; }
nocr() { tr -d '\r'; }

SLNS="src/backend/RoomLedger.sln src/infra/RoomLedger.Cdk.Tests/RoomLedger.Cdk.Tests.csproj"
# Packages whose major version must match the runtime (8). EF Core / Npgsql 9.x restore on net8.0 without any error.
export PINNED='^(Microsoft\.(EntityFrameworkCore|AspNetCore)|Npgsql|EFCore\.)'

echo "== SDK"
if [ -f global.json ] && grep -Eq '"version"[[:space:]]*:[[:space:]]*"8\.0\.' global.json; then
  pass "global.json pins SDK 8.0.x"
else
  fail "global.json missing or not pinned to 8.0.x"
fi

sdk=$(dotnet --version 2>&1 | nocr | tail -1)
case "$sdk" in
  8.0.*) pass "dotnet --version = $sdk" ;;
  *)     fail "dotnet --version = $sdk (want 8.0.x; is a .NET 8 SDK installed?)" ;;
esac

echo "== Repo-local tools (.config/dotnet-tools.json)"
if [ -f .config/dotnet-tools.json ]; then
  if dotnet tool restore >/dev/null 2>&1; then pass "dotnet tool restore"; else fail "dotnet tool restore failed"; fi
  ef=$(dotnet ef --version 2>&1 | nocr | grep -Eo '^[0-9]+\.[0-9]+\.[0-9]+' | tail -1)
  case "$ef" in
    8.0.*) pass "dotnet ef = $ef" ;;
    *)     fail "dotnet ef = ${ef:-not found} (want 8.0.x)" ;;
  esac
  if dotnet lambda help >/dev/null 2>&1; then pass "dotnet lambda (Amazon.Lambda.Tools) runs"; else fail "dotnet lambda not available"; fi
else
  fail ".config/dotnet-tools.json missing (dotnet-ef 8.x + Amazon.Lambda.Tools must be pinned per repo)"
fi

mapfile -t PROJECTS < <(find src/backend src/infra -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | sort)

echo "== Every project targets net8.0 (${#PROJECTS[@]} projects)"
# Read the evaluated value, so TargetFramework set in Directory.Build.props counts.
others=""
for f in "${PROJECTS[@]}"; do
  tfms=$(dotnet msbuild "$f" -getProperty:TargetFramework -getProperty:TargetFrameworks 2>/dev/null | nocr \
         | grep -Eo '"TargetFrameworks?": *"[^"]*"' | sed -E 's/.*: *"([^"]*)"/\1/' | tr ';' '\n' | sed '/^$/d' | sort -u)
  if [ -z "$tfms" ] || printf '%s\n' "$tfms" | grep -qvx 'net8\.0'; then
    others="$others $f [$(printf '%s' "$tfms" | tr '\n' ';')]"
  fi
done
if [ -z "$others" ]; then pass "all net8.0 only"; else fail "not net8.0-only:$others"; fi

echo "== Pinned packages are 8.x (inline Version= in csproj)"
bad=$(grep -HEo '<PackageReference Include="[^"]+" Version="[^"]+"' "${PROJECTS[@]}" \
      | sed -E 's/^([^:]+):.*Include="([^"]+)" Version="([^"]+)"/\2 \3 \1/' \
      | awk '$1 ~ ENVIRON["PINNED"] && $2 !~ /^8\./')
if [ -z "$bad" ]; then pass "every direct EF/Npgsql/ASP.NET Core reference is 8.x"; else fail "not 8.x: $(echo "$bad" | tr '\n' ';')"; fi

echo "== Resolved packages (incl. transitive) are 8.x"
resolved=""
for s in $SLNS; do
  if dotnet restore "$s" >/dev/null 2>&1; then
    resolved="$resolved
$(dotnet list "$s" package --include-transitive 2>/dev/null | nocr | awk '$1 == ">" { print $2, $NF }')"
  else
    fail "dotnet restore $s failed"
  fi
done
over=$(printf '%s\n' "$resolved" | sort -u | awk '$1 ~ ENVIRON["PINNED"] && $2 !~ /^8\./')
n=$(printf '%s\n' "$resolved" | sort -u | awk '$1 ~ ENVIRON["PINNED"]' | wc -l | tr -d ' ')
if [ "$n" -gt 0 ] && [ -z "$over" ]; then
  pass "all $n resolved EF/Npgsql/ASP.NET Core packages are 8.x"
else
  fail "resolved packages not on 8.x (or none found): $(echo "$over" | tr '\n' ';')"
fi

echo "== Machine tools (warn only; not needed in CI)"
node_major=$(node --version 2>/dev/null | nocr | sed -E 's/^v([0-9]+).*/\1/')
if [ -n "$node_major" ] && [ "$node_major" -ge 20 ]; then pass "node v$node_major"; else warn "Node 20+ not found"; fi
command -v cdk    >/dev/null 2>&1 && pass "cdk $(cdk --version 2>/dev/null | nocr | cut -d' ' -f1)" || warn "aws-cdk not installed (npm install -g aws-cdk)"
command -v aws    >/dev/null 2>&1 && pass "$(aws --version 2>&1 | nocr | cut -d' ' -f1)"         || warn "AWS CLI v2 not installed"
if ! command -v docker >/dev/null 2>&1; then warn "Docker not installed (needed for the local test database)"
elif docker info >/dev/null 2>&1; then pass "docker daemon running"
else warn "Docker installed but daemon not running (start Docker Desktop before integration tests)"; fi

echo
if [ "$fails" -eq 0 ]; then echo "T0 RESULT: GREEN"; exit 0; else echo "T0 RESULT: RED ($fails failing check(s))"; exit 1; fi
