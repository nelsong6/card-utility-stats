#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }

# Tripwires for room-type-gated scenario staging (spirelens#146). An
# Elite/Boss-gated relic (e.g. Booming Conch, "at the start of Elite combats")
# must be staged in a real Elite/Boss room and the scenario must REFUSE to pass
# in the wrong room type — instead of coercing it through next_normal_encounter,
# which only selects which Monster encounter is fought and produced a normal
# Nibbit room where the relic never fired.
#
# These guard the source text rather than dot-sourcing because the staging
# lives in an embedded Python here-string (prepare-scenario.ps1) and a prompt
# string (run-phases.ps1), neither of which is an AST-extractable function. The
# behavioral logic itself is covered by the embedded-Python validator.

BeforeAll {
    Set-StrictMode -Version 3.0
    $ErrorActionPreference = 'Stop'
    $script:RunPhases = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..' 'run-phases.ps1') -Raw
    $script:PrepareScenario = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..' 'prepare-scenario.ps1') -Raw
}

Describe 'test-plan prompt room-type guidance' {
    It 'documents required_room_type as the lever for a room-type-gated trigger' {
        $script:RunPhases | Should -Match 'required_room_type'
        $script:RunPhases | Should -Match 'at the start of Elite combats'
    }
    It 'no longer routes encounter-type-conditional triggers at next_normal_encounter' {
        # The exact phrase that sent Booming Conch into a normal Nibbit room.
        $script:RunPhases | Should -Not -Match 'encounter-type-conditional triggers'
    }
    It 'declares next_normal_encounter as Monster-only' {
        $script:RunPhases | Should -Match 'Monster \(normal\) encounter'
    }
    It 'includes required_room_type in the scenario_setup JSON template' {
        $script:RunPhases | Should -Match '"required_room_type":null'
    }
}

Describe 'prepare-scenario room-type staging' {
    It 'enters the required room type deterministically via enter_debug_room' {
        $script:PrepareScenario | Should -Match 'enter_debug_room'
        $script:PrepareScenario | Should -Match 'required_room_type'
    }
    It 'fails the scenario when the live room type does not match the requirement' {
        $script:PrepareScenario | Should -Match 'state_type != required_room_type'
    }
    It 'treats elite and boss as combat room types in the readiness check' {
        $script:PrepareScenario | Should -Match "'elite', 'boss'"
    }
}
