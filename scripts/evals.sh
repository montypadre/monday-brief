#!/usr/bin/env bash
# Runs the live eval suite and writes eval-report.md. Costs roughly $0.30 per run.
set -euo pipefail

ANTHROPIC_API_KEY=$(dotnet user-secrets --project src/MondayBrief.Api list | grep "Anthropic:ApiKey" | cut -d= -f2 | tr -d ' ') \
RUN_EVALS=1 \
dotnet test --filter "FullyQualifiedName=MondayBrief.Evals.Live.LiveEvalTests.Eval_suite_passes_the_mark"
