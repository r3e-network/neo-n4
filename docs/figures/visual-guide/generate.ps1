param(
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

function Esc([string]$Text) {
    if ($null -eq $Text) { return "" }
    return [System.Security.SecurityElement]::Escape($Text)
}

function Lines([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return @() }
    return $Text -split "\|"
}

function TextBlock([int]$X, [int]$Y, [string]$Text, [string]$Class = "body", [int]$LineHeight = 18, [string]$Anchor = "start") {
    $LineArray = Lines $Text
    if ($LineArray.Count -eq 0) { return "" }
    $items = @()
    for ($i = 0; $i -lt $LineArray.Count; $i++) {
        $dy = if ($i -eq 0) { 0 } else { $LineHeight }
        $items += "<tspan x=""$X"" dy=""$dy"">$(Esc $LineArray[$i])</tspan>"
    }
    return "<text x=""$X"" y=""$Y"" class=""$Class"" text-anchor=""$Anchor"">$($items -join '')</text>"
}

function Box([int]$X, [int]$Y, [int]$W, [int]$H, [string]$Title, [string]$Body, [string]$Fill, [string]$Stroke = "#d8dee8") {
    $titleY = $Y + 27
    $bodyY = $Y + 52
    @"
<g>
  <rect x="$X" y="$Y" width="$W" height="$H" rx="8" fill="$Fill" stroke="$Stroke" filter="url(#card)"/>
  <text x="$($X + 16)" y="$titleY" class="box-title">$(Esc $Title)</text>
  $(TextBlock ($X + 16) $bodyY $Body "body")
</g>
"@
}

function SmallBox([int]$X, [int]$Y, [int]$W, [int]$H, [string]$Title, [string]$Fill, [string]$Stroke = "#d8dee8") {
    $ty = $Y + [int]($H / 2) + 5
    "<rect x=""$X"" y=""$Y"" width=""$W"" height=""$H"" rx=""7"" fill=""$Fill"" stroke=""$Stroke""/><text x=""$($X + $W / 2)"" y=""$ty"" text-anchor=""middle"" class=""small-title"">$(Esc $Title)</text>"
}

function Arrow([int]$X1, [int]$Y1, [int]$X2, [int]$Y2, [string]$Label = "", [string]$Class = "arrow") {
    $mx = [int](($X1 + $X2) / 2)
    $my = [int](($Y1 + $Y2) / 2) - 8
    $labelNode = ""
    if ($Label -ne "") {
        $labelNode = "<text x=""$mx"" y=""$my"" text-anchor=""middle"" class=""arrow-label"">$(Esc $Label)</text>"
    }
    "<line x1=""$X1"" y1=""$Y1"" x2=""$X2"" y2=""$Y2"" class=""$Class"" marker-end=""url(#arrow)""/>$labelNode"
}

function DashedArrow([int]$X1, [int]$Y1, [int]$X2, [int]$Y2, [string]$Label = "") {
    Arrow $X1 $Y1 $X2 $Y2 $Label "arrow dashed"
}

function Lane([int]$X, [int]$Y, [int]$W, [int]$H, [string]$Title, [string]$Fill) {
    @"
<g>
  <rect x="$X" y="$Y" width="$W" height="$H" rx="8" fill="$Fill" stroke="#d8dee8"/>
  <text x="$($X + $W / 2)" y="$($Y + 30)" text-anchor="middle" class="lane-title">$(Esc $Title)</text>
  <line x1="$($X + $W / 2)" y1="$($Y + 48)" x2="$($X + $W / 2)" y2="$($Y + $H - 18)" class="lifeline"/>
</g>
"@
}

function RenderSvg([string]$FileName, [int]$W, [int]$H, [string]$Title, [string]$Desc, [string]$Body) {
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 $W $H" width="$W" height="$H" font-family="'Noto Sans CJK SC', -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif" role="img" aria-labelledby="title desc">
  <title id="title">$(Esc $Title)</title>
  <desc id="desc">$(Esc $Desc)</desc>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto">
      <path d="M0,0 L10,5 L0,10 Z" fill="#526173"/>
    </marker>
    <filter id="card" x="-2%" y="-3%" width="104%" height="106%">
      <feDropShadow dx="0" dy="1" stdDeviation="1.0" flood-color="#000000" flood-opacity="0.08"/>
    </filter>
    <style>
      .bg { fill: #fbfcfe; }
      .page-title { font-size: 22px; font-weight: 750; fill: #1e2a55; }
      .page-subtitle { font-size: 12.5px; fill: #526173; }
      .box-title { font-size: 14px; font-weight: 750; fill: #1e2a55; }
      .small-title { font-size: 12px; font-weight: 700; fill: #1e2a55; }
      .lane-title { font-size: 13px; font-weight: 750; fill: #1e2a55; }
      .body { font-size: 11.5px; fill: #2d3a48; }
      .body-strong { font-size: 11.5px; font-weight: 700; fill: #1e2a55; }
      .caption { font-size: 11px; fill: #526173; font-style: italic; }
      .arrow { stroke: #526173; stroke-width: 1.7; }
      .dashed { stroke-dasharray: 6 5; }
      .arrow-label { font-size: 10.5px; fill: #526173; font-weight: 650; }
      .lifeline { stroke: #c7d0dc; stroke-width: 1.1; stroke-dasharray: 4 4; }
      .zone-title { font-size: 13px; font-weight: 750; fill: #526173; letter-spacing: .2px; }
      .mono { font-family: 'Noto Sans Mono CJK SC', ui-monospace, SFMono-Regular, Consolas, monospace; font-size: 10.5px; fill: #1e2a55; }
    </style>
  </defs>
  <rect class="bg" width="$W" height="$H"/>
  <text x="36" y="40" class="page-title">$(Esc $Title)</text>
  <text x="36" y="63" class="page-subtitle">$(Esc $Desc)</text>
$Body
</svg>
"@
    Set-Content -Path (Join-Path $OutDir $FileName) -Value $svg -Encoding UTF8
}

$blue = "#eaf2ff"
$green = "#eaf7f0"
$orange = "#fff5e6"
$purple = "#f7ecff"
$red = "#ffecec"
$gray = "#f4f6f8"
$cyan = "#e7f8fb"
$yellow = "#fff9d9"

RenderSvg "system-context.svg" 1200 720 "Neo N4 System Context" "Users, NeoHub L1, elastic L2 chains, foreign chains, and operator services in one view." @"
$(Box 55 105 210 125 'Users and wallets' 'Deposit and withdraw|Sign governance tx|Read SDK/RPC state' $blue)
$(Box 360 105 250 165 'NeoHub L1 anchor' 'SharedBridge / SettlementManager|ChainRegistry / TokenRegistry|VerifierRegistry / Governance|ExternalBridgeEscrow' $green)
$(Box 725 105 220 145 'Elastic L2 chain' 'Neo 4 core + L2 plugins|L2 native contracts|Execution, batches, state roots' $cyan)
$(Box 55 390 245 135 'Foreign networks' 'EVM family / Tron / Solana|Foreign router contracts|Lock, release, emit events' $purple)
$(Box 405 405 250 145 'Off-chain runtime' 'Sequencer / Batcher / Prover|DA writer / Bridge watcher|Telemetry + health checks' $orange)
$(Box 770 405 250 135 'Developer entrypoints' 'C# / TypeScript / Rust SDK|neo-stack / neo-bridge CLI|docs + generated artifacts' $gray)
$(Arrow 265 168 360 168 'signed tx')
$(Arrow 610 168 725 168 'messages / roots')
$(DashedArrow 835 250 530 405 'batches + proofs')
$(Arrow 300 458 405 458 'foreign events')
$(Arrow 655 458 770 458 'operator APIs')
$(DashedArrow 176 390 176 230 'wallet bridge')
$(DashedArrow 495 405 495 270 'settlement tx')
<text x="55" y="650" class="caption">Solid lines mean on-chain state changes; dashed lines mean off-chain proofs, event watching, or operator control flow.</text>
"@

RenderSvg "module-map.svg" 1200 760 "Code Module Map" "Repository directories mapped to runtime responsibilities and deployable artifacts." @"
$(Box 45 105 230 140 'contracts/' '28 product smart contracts|NeoHub L1 + L2Native|nccs -> bin/sc artifacts' $green)
$(Box 330 105 235 140 'src/' 'L2 libraries and plugins|Batch / State / Bridge / DA|RPC, proving, telemetry' $blue)
$(Box 620 105 230 140 'tools/' 'neo-hub-deploy|neo-stack / neo-bridge|faucet / explorer / devnet' $orange)
$(Box 905 105 225 140 'tests/' 'unit + integration coverage|CLI and plugin assertions|Windows runtime checks' $gray)
$(Box 45 335 230 140 'watchers/' 'ETH live daemon|Solana + Tron wrappers|journal / health / metrics' $purple)
$(Box 330 335 235 140 'external/' 'Neo submodules|EVM Foundry router|Solana Anchor router' $red)
$(Box 620 335 230 140 'sdk/' 'TypeScript client|Rust client|proof and RPC helpers' $cyan)
$(Box 905 335 225 140 'docs/' 'mdBook|architecture atlas|visual guide SVGs' $yellow)
$(Arrow 275 175 330 175 'refs')
$(Arrow 565 175 620 175 'uses')
$(Arrow 850 175 905 175 'tests')
$(Arrow 160 245 160 335 'watch')
$(Arrow 445 245 445 335 'submodules')
$(Arrow 735 245 735 335 'client APIs')
$(Arrow 1018 245 1018 335 'docs')
<rect x="45" y="545" width="1085" height="120" rx="8" fill="#ffffff" stroke="#d8dee8" filter="url(#card)"/>
<text x="65" y="580" class="box-title">Production artifact paths</text>
<text x="65" y="610" class="mono">contracts/**/bin/sc/*.nef</text>
<text x="355" y="610" class="mono">contracts/**/bin/sc/*.manifest.json</text>
<text x="720" y="610" class="mono">tools/Neo.Hub.Deploy -> deploy-bundle.json</text>
"@

RenderSvg "deposit-flow.svg" 1200 650 "L1 to L2 Deposit Flow" "The user locks an L1 asset; the L2 bridge mints the corresponding wrapped asset from the event payload." @"
$(Lane 55 110 170 470 'L1 wallet' $blue)
$(Lane 270 110 190 470 'SharedBridge' $green)
$(Lane 505 110 190 470 'Event watcher / Batcher' $orange)
$(Lane 740 110 190 470 'L2BridgeContract' $cyan)
$(Lane 975 110 170 470 'L2 balance' $blue)
$(Arrow 140 170 365 170 '1. Deposit(chainId, asset, amount, recipient)')
$(SmallBox 285 205 160 42 'lock L1 asset' $green)
$(Arrow 365 252 600 252 '2. DepositEnqueued event')
$(SmallBox 520 288 160 42 'make L2 message' $orange)
$(Arrow 600 335 835 335 '3. relay deposit payload')
$(SmallBox 755 372 160 42 'mint wrapped asset' $cyan)
$(Arrow 835 420 1060 420 '4. credit recipient')
<rect x="250" y="500" width="705" height="60" rx="8" fill="#fff9d9" stroke="#ebd37b"/>
<text x="270" y="525" class="body-strong">Invariant</text>
<text x="350" y="525" class="body">L1 locked amount equals L2 wrapped supply; payload carries chainId, asset, recipient, and amount.</text>
"@

RenderSvg "withdrawal-flow.svg" 1200 690 "L2 to L1 Withdrawal Flow" "The withdrawal leaf is bound to the full preimage before SharedBridge releases L1 assets." @"
$(Lane 45 115 165 500 'L2 user' $blue)
$(Lane 250 115 180 500 'L2BridgeContract' $cyan)
$(Lane 470 115 190 500 'Batch / state root' $orange)
$(Lane 700 115 185 500 'SettlementManager' $green)
$(Lane 925 115 220 500 'SharedBridge + TokenRegistry' $green)
$(Arrow 128 175 340 175 '1. Withdraw(l2Asset, amount, l1Recipient)')
$(SmallBox 270 215 140 42 'burn wrapped asset' $cyan)
$(Arrow 340 265 565 265 '2. WithdrawalEmitted leaf')
$(SmallBox 492 305 150 46 'seal in batch root' $orange)
$(Arrow 565 355 792 355 '3. submit + finalize batch')
$(SmallBox 715 395 155 46 'root becomes final' $green)
$(Arrow 792 447 1035 447 '4. FinalizeWithdrawalWithProof')
<rect x="930" y="490" width="210" height="78" rx="8" fill="#ffecec" stroke="#e8a6a6"/>
<text x="948" y="518" class="body-strong">Safety binding</text>
<text x="948" y="540" class="body">recompute withdrawal leaf</text>
<text x="948" y="558" class="body">check TokenRegistry mapping</text>
<text x="70" y="642" class="caption">Audit fix: leaf inclusion alone is not enough; payout parameters must be the canonical leaf preimage.</text>
"@

RenderSvg "batch-lifecycle.svg" 1200 700 "Batch Settlement Lifecycle" "From L2 transaction ordering to L1 roots, proof verification, challenge windows, and finality." @"
$(Box 50 120 185 105 'Tx pool' 'user txs|cross-chain messages|forced inclusion' $blue)
$(Box 280 120 185 105 'Sequencer' 'ordering|timestamps|batch boundary' $orange)
$(Box 510 120 185 105 'Executor' 'Neo VM execution|state transition|receipts' $cyan)
$(Box 740 120 185 105 'Batch commitment' 'prevRoot -> newRoot|message roots|public inputs' $yellow)
$(Box 970 120 175 105 'DA writer' 'publish payload|DARegistry ref|availability proof' $purple)
$(Box 165 380 200 115 'SettlementManager' 'submitBatch|batch metadata|finality status' $green)
$(Box 455 380 210 115 'VerifierRegistry' 'validity / fraud verifier|proof dispatch|versioned policy' $green)
$(Box 755 380 220 115 'OptimisticChallenge' 'challenge window|fraud proof|SequencerBond slash' $red)
$(Arrow 235 172 280 172 '1')
$(Arrow 465 172 510 172 '2')
$(Arrow 695 172 740 172 '3')
$(Arrow 925 172 970 172 '4')
$(DashedArrow 830 225 265 380 '5. submit to L1')
$(Arrow 365 438 455 438 'verify')
$(Arrow 665 438 755 438 'challenge')
$(DashedArrow 865 380 865 225 'slash / finality feedback')
"@

RenderSvg "external-bridge-flow.svg" 1200 720 "External Bridge Data Flow" "Foreign-chain lock events become NeoHub inbound messages through watchers, committee signatures, and verifier contracts." @"
$(Box 55 120 225 135 'Foreign router' 'EVM / Tron / Solana|Lock / finalize|emits canonical event' $purple)
$(Box 355 120 225 135 'Watcher daemon' 'poll RPC|decode message|journal nonce' $orange)
$(Box 660 120 215 135 'MPC committee' 'sign message hash|threshold proof|curve-specific pubkeys' $yellow)
$(Box 945 120 205 135 'NeoHub verifier' 'MpcCommitteeVerifier|verify inbound proof|bind externalChainId' $green)
$(Box 230 415 240 135 'ExternalBridgeEscrow' 'locks / releases NEP-17|checks registry route|records replay guard' $green)
$(Box 560 415 230 135 'ExternalBridgeRegistry' 'externalChainId -> verifier|governance upgrade|bridge kind' $green)
$(Box 875 415 230 135 'ExternalBridgeBond' 'committee bond|fraud verifier slashing|governance controls' $red)
$(Arrow 280 188 355 188 'event')
$(Arrow 580 188 660 188 'message hash')
$(Arrow 875 188 945 188 'proofBytes')
$(DashedArrow 1048 255 350 415 'Receive(...)')
$(Arrow 470 482 560 482 'lookup route')
$(Arrow 790 482 875 482 'slash on equivocation')
$(DashedArrow 350 550 170 255 'outbound: lock on Neo -> release foreign')
"@

RenderSvg "deployment-pipeline.svg" 1200 660 "Production Deployment Pipeline" "From C# contract projects to NEF/manifest artifacts and a wallet-signable NeoHub deploy bundle." @"
$(Box 55 125 220 115 'Contract projects' 'contracts/**/*.csproj|Neo.SmartContract.Framework|warn-as-error build' $blue)
$(Box 330 125 190 115 'nccs' 'Neo.Compiler.CSharp|project direct build|CI artifact loop' $orange)
$(Box 575 125 225 115 'bin/sc artifacts' '*.nef|*.manifest.json|actual deploy inputs' $green)
$(Box 855 125 230 115 'neo-hub-deploy' 'scaffold plan|topological sort|resolve step hashes' $cyan)
$(Box 205 395 230 115 'deploy-plan.json' 'editable placeholders|OWNER_REPLACE_ME|BOND_ASSET_REPLACE_ME' $gray)
$(Box 505 395 230 115 'deploy-bundle.json' '20 production invocations|nefPath / manifestPath|resolved deploy data' $yellow)
$(Box 805 395 235 115 'Wallet + L1 deploy' 'sign each invocation|ContractManagement.Deploy|post-deploy wiring' $green)
$(Arrow 275 183 330 183 'compile')
$(Arrow 520 183 575 183 'emit')
$(Arrow 800 183 855 183 'read')
$(DashedArrow 970 240 320 395 'scaffold')
$(Arrow 435 452 505 452 'plan')
$(Arrow 735 452 805 452 'sign')
<text x="56" y="600" class="caption">Key consistency rule: the default deploy bundle must reference bin/sc, not bin/Release.</text>
"@

RenderSvg "data-structures.svg" 1200 770 "Core Data Structures" "The bridge, settlement, deployment, and watcher records that carry consensus-relevant data." @"
$(Box 45 110 250 150 'WithdrawalLeaf' 'emittingContract: UInt160|l2Sender: UInt160|l1Recipient: UInt160|l2Asset: UInt160|amount: BigInteger LE|nonce: UInt64 LE' $red)
$(Box 335 110 250 150 'ExternalCrossChainMessage' 'externalChainId: UInt32|direction + messageType|nonce: UInt64|sender / recipient|payload bytes' $purple)
$(Box 625 110 250 150 'L2BatchCommitment' 'batchId|prevStateRoot / newStateRoot|withdrawalRoot|messageRoot|txRoot / timestamp' $yellow)
$(Box 915 110 240 150 'L2ChainConfig' 'chainId|rollup mode|sequencer policy|DA mode|verifier set' $blue)
$(Box 185 410 260 150 'PublicInputs' 'batch commitment hash|pre / post roots|DA reference|proof version|chain identity' $cyan)
$(Box 520 410 265 150 'DeployInvocation' 'name|nefPath|manifestPath|resolvedDeployData|topological order' $green)
$(Box 860 410 250 150 'JournalRecord' 'cursor block|externalChainId|nonce|consumed log|idempotent replay guard' $orange)
$(Arrow 295 185 335 185 'hash')
$(Arrow 585 185 625 185 'batch')
$(Arrow 750 260 315 410 'proof input')
$(Arrow 1035 260 652 410 'deploy config')
$(Arrow 585 485 520 485 'bundle')
$(DashedArrow 985 410 460 185 'watcher replay protection')
"@

RenderSvg "trust-boundaries-map.svg" 1200 720 "Trust Boundary Map" "Components grouped by trust domain; every cross-domain action requires proof, signature threshold, governance, or registry binding." @"
<rect x="45" y="110" width="250" height="500" rx="12" fill="#eaf2ff" stroke="#a9bce1"/>
<text x="65" y="140" class="zone-title">User-controlled domain</text>
$(SmallBox 75 175 190 44 'Wallet / SDK' '#ffffff')
$(SmallBox 75 245 190 44 'User funds' '#ffffff')
$(SmallBox 75 315 190 44 'Signed tx intent' '#ffffff')
<rect x="330" y="110" width="265" height="500" rx="12" fill="#fff5e6" stroke="#e4c17d"/>
<text x="350" y="140" class="zone-title">Operator-controlled domain</text>
$(SmallBox 365 175 195 44 'Sequencer / Batcher' '#ffffff')
$(SmallBox 365 245 195 44 'Watcher daemon' '#ffffff')
$(SmallBox 365 315 195 44 'Prover / DA writer' '#ffffff')
<rect x="630" y="110" width="245" height="500" rx="12" fill="#eaf7f0" stroke="#9fd3b1"/>
<text x="650" y="140" class="zone-title">On-chain verified domain</text>
$(SmallBox 665 175 175 44 'NeoHub contracts' '#ffffff')
$(SmallBox 665 245 175 44 'Token registry' '#ffffff')
$(SmallBox 665 315 175 44 'Verifier registry' '#ffffff')
<rect x="910" y="110" width="245" height="500" rx="12" fill="#f7ecff" stroke="#c4a1dd"/>
<text x="930" y="140" class="zone-title">Foreign-chain domain</text>
$(SmallBox 945 175 175 44 'Foreign router' '#ffffff')
$(SmallBox 945 245 175 44 'MPC committee' '#ffffff')
$(SmallBox 945 315 175 44 'External RPCs' '#ffffff')
$(Arrow 265 197 365 197 'signed op')
$(Arrow 560 267 665 267 'proof / root')
$(Arrow 840 197 945 197 'message proof')
$(DashedArrow 460 359 752 359 'governance + slashing controls')
$(DashedArrow 1032 315 462 315 'RPC data is untrusted until verified')
<text x="55" y="660" class="caption">Off-chain components may produce data, but accounting changes cross domains only through on-chain checks.</text>
"@

RenderSvg "watcher-state-machine.svg" 1200 720 "Watcher Daemon State Machine" "The live-rpc ETH watcher path: startup, preflight, polling, proof construction, submission, journal, and recovery." @"
$(SmallBox 70 140 160 52 'load config' $blue)
$(SmallBox 290 140 160 52 'preflight' $green)
$(SmallBox 510 140 160 52 'poll RPC' $orange)
$(SmallBox 730 140 160 52 'decode event' $purple)
$(SmallBox 950 140 160 52 'build message' $yellow)
$(SmallBox 185 360 170 52 'sign proof' $yellow)
$(SmallBox 430 360 170 52 'Neo pre-check' $green)
$(SmallBox 675 360 170 52 'sign + submit' $cyan)
$(SmallBox 920 360 170 52 'journal cursor' $orange)
$(Arrow 230 166 290 166 '')
$(Arrow 450 166 510 166 '')
$(Arrow 670 166 730 166 '')
$(Arrow 890 166 950 166 '')
$(Arrow 1030 192 270 360 '')
$(Arrow 355 386 430 386 '')
$(Arrow 600 386 675 386 '')
$(Arrow 845 386 920 386 '')
$(DashedArrow 1005 360 590 192 'next tick')
<rect x="430" y="545" width="350" height="68" rx="8" fill="#ffecec" stroke="#e8a6a6"/>
<text x="455" y="573" class="body-strong">error path</text>
<text x="545" y="573" class="body">transport / RPC / submit failures record health error, back off, then retry.</text>
$(DashedArrow 600 545 600 412 'backoff')
<text x="70" y="655" class="caption">Windows tests cover preflight; Unix tests also cover SIGTERM graceful shutdown.</text>
"@

RenderSvg "testing-matrix.svg" 1200 720 "Verification Matrix" "Build, test, artifact, and dependency checks by runtime surface." @"
$(Box 55 110 245 140 '.NET solution' '101 projects build|unit + integration tests|package vulnerability audit' $blue)
$(Box 335 110 245 140 'Smart contracts' '28 product + 2 sample projects|direct nccs artifact generation|NEF + manifest completeness' $green)
$(Box 615 110 245 140 'Rust watchers / SDK' 'ETH live-rpc tests + clippy|Solana / Tron parity|cargo audit' $orange)
$(Box 895 110 245 140 'Foreign contracts' 'Foundry EVM tests|Solana Anchor tests|router state isolation' $purple)
$(Box 195 355 245 140 'TypeScript SDK' 'vitest|tsc|npm audit' $cyan)
$(Box 475 355 245 140 'Documentation' 'mdBook build|SVG visual assets|operator docs consistency' $yellow)
$(Box 755 355 245 140 'Security hygiene' 'withdrawal preimage binding|secret scan|dependency advisory tracking' $red)
$(Arrow 300 180 335 180 'build')
$(Arrow 580 180 615 180 'cross-check')
$(Arrow 860 180 895 180 'parity')
$(DashedArrow 615 250 315 355 'SDK docs')
$(DashedArrow 735 250 595 355 'visual docs')
$(DashedArrow 895 355 1018 250 'bridge tests')
<text x="55" y="645" class="caption">Production readiness means every runtime surface has build, tests, dependency audit, or artifact completeness checks.</text>
"@

RenderSvg "operator-runbook.svg" 1200 700 "Operator Runbook Flow" "End-to-end launch path from toolchain setup to NeoHub deploy, L2 registration, bridge adapters, watchers, and telemetry." @"
$(SmallBox 65 130 185 52 '1. install toolchains' $blue)
$(SmallBox 315 130 185 52 '2. build + test' $green)
$(SmallBox 565 130 185 52 '3. generate artifacts' $orange)
$(SmallBox 815 130 220 52 '4. deploy NeoHub bundle' $green)
$(SmallBox 185 335 200 52 '5. register L2 chain' $cyan)
$(SmallBox 455 335 220 52 '6. deploy L2 adapters' $cyan)
$(SmallBox 745 335 220 52 '7. configure watchers' $purple)
$(SmallBox 455 535 220 52 '8. enable telemetry' $yellow)
$(Arrow 250 156 315 156 '')
$(Arrow 500 156 565 156 '')
$(Arrow 750 156 815 156 '')
$(Arrow 925 182 285 335 '')
$(Arrow 385 361 455 361 '')
$(Arrow 675 361 745 361 '')
$(Arrow 855 387 565 535 '')
<rect x="210" y="610" width="760" height="46" rx="8" fill="#ffffff" stroke="#d8dee8"/>
<text x="230" y="638" class="body">Gate: do not open deposits until post-deploy wiring, registry bindings, watcher preflight, and health checks are green.</text>
"@

RenderSvg "project-artifact-map.svg" 1200 720 "Source to Artifact Map" "Where source, tests, docs, deploy artifacts, and audit evidence connect." @"
$(Box 65 120 215 120 'Source' 'contracts/|src/|watchers/|sdk/' $blue)
$(Box 350 120 215 120 'Build' 'dotnet build|cargo test/clippy|npm build|forge / anchor' $orange)
$(Box 635 120 215 120 'Artifacts' '*.dll|*.nef|*.manifest.json|deploy-bundle.json' $green)
$(Box 920 120 215 120 'Runtime' 'Neo node plugins|NeoHub contracts|watcher daemons|client SDKs' $purple)
$(Box 190 390 240 120 'Tests' 'tests/|parity vectors|integration smoke|CLI checks' $gray)
$(Box 520 390 240 120 'Docs' 'mdBook pages|architecture atlas|visual guide|operator runbooks' $yellow)
$(Box 850 390 240 120 'Audit evidence' 'package audits|secret scan|verification report|residual notes' $red)
$(Arrow 280 180 350 180 'compile')
$(Arrow 565 180 635 180 'emit')
$(Arrow 850 180 920 180 'deploy')
$(DashedArrow 458 240 310 390 'assert')
$(DashedArrow 742 240 640 390 'explain')
$(DashedArrow 1028 240 970 390 'prove')
"@

$readme = @'
# Visual guide figures

Generated by `generate.ps1`.

These SVGs provide a user-friendly high-level map of Neo N4:

- `system-context.svg`
- `module-map.svg`
- `deposit-flow.svg`
- `withdrawal-flow.svg`
- `batch-lifecycle.svg`
- `external-bridge-flow.svg`
- `deployment-pipeline.svg`
- `data-structures.svg`
- `trust-boundaries-map.svg`
- `watcher-state-machine.svg`
- `testing-matrix.svg`
- `operator-runbook.svg`
- `project-artifact-map.svg`
'@
Set-Content -Path (Join-Path $OutDir "README.md") -Value $readme -Encoding UTF8
