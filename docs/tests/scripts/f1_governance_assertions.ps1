#requires -Version 5.1
# Definitions only. Called exclusively by the focused suite with -IncludeGovernance after approval.
# Uses the suite's guarded API client, credentials, in-memory concurrency and fail-fast assertions.
function Invoke-F1GovernanceAcceptance {
  param([string]$SuperToken,[int]$SuperId,[string]$InvestorToken,[int]$InvestorId,[string]$LandlordToken)
  $marker=[guid]::NewGuid().ToString('N')
  $rolesResponse=Invoke-VerificationApi -Method GET -Path '/api/roles' -BearerToken $SuperToken
  Assert-Exact 'governance role lookup is authorized' 200 $rolesResponse | Out-Null
  $adminRole=@(ConvertFrom-JsonArray $rolesResponse.Body | Where-Object {$_.name -eq 'Admin'})
  Assert-True 'Admin role resolves uniquely from API' ($adminRole.Count -eq 1)
  $email="f1.gov.$marker.admin@example.test"
  $baseline=New-OutboxBaseline
  $body=New-AccountJson -FullName "F1 governance $marker" -Email $email -RoleId ([int]$adminRole[0].id)
  $created=Invoke-VerificationApi -Method POST -Path '/api/users' -BearerToken $SuperToken -Json $body
  Assert-Exact 'governance Admin created through authorized workflow' 201 $created | Out-Null
  $adminId=Get-RequiredPositiveId -Item ($created.Body | ConvertFrom-Json) -Label 'governance Admin'
  $blocked=Invoke-Login -Email $email -Password $script:TestPassword
  Assert-Exact 'Admin has no usable password before activation' 401 $blocked | Out-Null
  $activation=Activate-AdminAccount -Email $email -Password $script:TestPassword -Baseline $baseline
  Assert-Exact 'Admin activates through its actual invitation' 204 $activation | Out-Null
  $login=Invoke-Login -Email $email -Password $script:TestPassword
  Assert-Exact 'activated Admin signs in' 200 $login | Out-Null
  $adminToken=[string](JsonField $login.Body 'token')
  $adminName=[string]($login.Body | ConvertFrom-Json).user.fullName

  $investorBefore=Invoke-VerificationApi -Method GET -Path ("/api/users/$InvestorId") -BearerToken $adminToken
  Assert-Exact 'Admin reads verified investor before overwrite attempt' 200 $investorBefore | Out-Null
  $investor=$investorBefore.Body | ConvertFrom-Json
  $overwrite=Invoke-VerificationApi -Method PUT -Path ("/api/users/$InvestorId") -BearerToken $adminToken -Json (@{
    fullName=$investor.fullName;phone=$investor.phone;email="f1.gov.$marker.override@example.test"
  } | ConvertTo-Json -Compress)
  Assert-Exact 'regular Admin direct verified-email overwrite returns 409' 409 $overwrite | Out-Null
  $investorAfter=Invoke-VerificationApi -Method GET -Path ("/api/users/$InvestorId") -BearerToken $adminToken
  Assert-Exact 'rejected Admin overwrite leaves account readable' 200 $investorAfter | Out-Null
  Assert-True 'rejected Admin overwrite preserves full identity and approval state' ($investorAfter.Body -ceq $investorBefore.Body)
  $session=Invoke-VerificationApi -Method GET -Path '/api/auth/me' -BearerToken $InvestorToken
  Assert-Exact 'rejected Admin overwrite preserves existing investor session' 200 $session | Out-Null
  $currentResponse=Invoke-VerificationApi -Method GET -Path '/api/criteria/current' -BearerToken $SuperToken
  Assert-Exact 'current criteria is readable' 200 $currentResponse | Out-Null
  $current=$currentResponse.Body | ConvertFrom-Json
  $oldHistory=Invoke-VerificationApi -Method GET -Path '/api/criteria/history' -BearerToken $SuperToken
  Assert-Exact 'criteria history is authorized' 200 $oldHistory | Out-Null
  $oldRows=@(ConvertFrom-JsonArray $oldHistory.Body)
  $fields=@('minAreaDonum','maxSlopePct','maxGridDistanceKm','minSolarIrradiance','minElevationM')
  $request=@{expectedCurrentId=[int]$current.id;reason="F1 governance $marker"}
  foreach($field in $fields){$request[$field]=$current.$field}
  $json=$request | ConvertTo-Json -Compress
  $anonymous=Invoke-VerificationApi -Method POST -Path '/api/criteria' -Json $json
  Assert-Exact 'anonymous criteria write is refused' 401 $anonymous | Out-Null
  foreach($role in @(@{Name='Admin';Token=$adminToken},@{Name='Investor';Token=$InvestorToken},@{Name='Landlord';Token=$LandlordToken})){
    $denied=Invoke-VerificationApi -Method POST -Path '/api/criteria' -BearerToken $role.Token -Json $json
    Assert-Exact ("criteria write refused for "+$role.Name) 403 $denied | Out-Null
  }
  # Read-only historical evidence. Do not create, edit or reinterpret an existing pinned land.
  $pinned=$null
  foreach($status in @('Verified','UnderContract')){
    $lands=Invoke-VerificationApi -Method GET -Path ("/api/lands?pageNumber=1&pageSize=50&status=$status") -BearerToken $SuperToken
    Assert-Exact 'pinned land inventory is authorized' 200 $lands | Out-Null
    foreach($land in @((ConvertFrom-Json $lands.Body).items)){
      if($null -ne $land.verifiedAgainstCriterionId){
        $candidate=Invoke-VerificationApi -Method GET -Path ("/api/lands/$($land.id)/assessments") -BearerToken $SuperToken
        Assert-Exact 'historical pinned assessment candidate is authorized' 200 $candidate | Out-Null
        if(@(ConvertFrom-JsonArray $candidate.Body).Count -gt 0){$pinned=$land;break}
      }
    }
    if($pinned){break}
  }
  Assert-True 'existing pinned-land evidence is available before criteria changes' ($null -ne $pinned)
  $assessmentBefore=Invoke-VerificationApi -Method GET -Path ("/api/lands/$($pinned.id)/assessments") -BearerToken $SuperToken
  Assert-Exact 'pinned assessment history is readable before replacement' 200 $assessmentBefore | Out-Null
  Assert-True 'pinned assessment preservation uses actual existing history' (@(ConvertFrom-JsonArray $assessmentBefore.Body).Count -gt 0)
  $eligibilityBefore=Invoke-VerificationApi -Method GET -Path ("/api/lands/$($pinned.id)/eligibility") -BearerToken $SuperToken
  Assert-Exact 'pinned eligibility is readable before replacement' 200 $eligibilityBefore | Out-Null

  # A real changed threshold, followed by a same-baseline race restoring original values.
  # Every row is uniquely marked and retained. No rollback/delete of history is attempted.
  $changed=if($null -eq $current.minAreaDonum -or [decimal]$current.minAreaDonum -eq 99999999.99){0.01}else{[decimal]$current.minAreaDonum+0.01}
  $request.minAreaDonum=$changed
  $first=Invoke-VerificationApi -Method POST -Path '/api/criteria' -BearerToken $SuperToken -Json ($request | ConvertTo-Json -Compress)
  Assert-Exact 'SuperAdmin appends an actual criteria change' 201 $first | Out-Null
  $version=$first.Body | ConvertFrom-Json
  Assert-True 'criteria records authenticated actor and immutable previous snapshot' ($version.previousCriterionId -eq $current.id -and $version.previousSnapshot.id -eq $current.id -and $version.updatedById -eq $SuperId -and $version.updatedByRole -eq 'SuperAdmin' -and $version.actorSnapshotRecorded -and $version.updatedAt)
  $delta=@($version.changes | Where-Object {$_.field -eq 'minAreaDonum'})
  Assert-True 'criteria comparison contains actual different old/new values' ($delta.Count -eq 1 -and $delta[0].oldValue -eq $current.minAreaDonum -and $delta[0].newValue -eq $changed)
  $request.expectedCurrentId=[int]$version.id
  $request.minAreaDonum=$current.minAreaDonum
  $raceJson=$request | ConvertTo-Json -Compress
  $race=Invoke-ConcurrentApiRequests -Requests @(
    @{Method='POST';Path='/api/criteria';BearerToken=$SuperToken;Json=$raceJson},
    @{Method='POST';Path='/api/criteria';BearerToken=$SuperToken;Json=$raceJson}
  )
  Assert-True 'same-baseline criteria race has one success and one conflict' (@($race | Where-Object {$_.Status -eq 201}).Count -eq 1 -and @($race | Where-Object {$_.Status -eq 409}).Count -eq 1)
  $history=Invoke-VerificationApi -Method GET -Path '/api/criteria/history' -BearerToken $SuperToken
  Assert-Exact 'criteria history remains readable' 200 $history | Out-Null
  $newRows=@(ConvertFrom-JsonArray $history.Body)
  Assert-True 'criteria race appends exactly two total versions without losing existing records' ($newRows.Count -eq $oldRows.Count+2)
  foreach($old in $oldRows){
    $retained=@($newRows | Where-Object {$_.id -eq $old.id})
    Assert-True 'old criteria record remains byte-for-byte equivalent in API history' ($retained.Count -eq 1 -and ($retained[0] | ConvertTo-Json -Depth 10 -Compress) -ceq ($old | ConvertTo-Json -Depth 10 -Compress))
  }
  $restored=Invoke-VerificationApi -Method GET -Path '/api/criteria/current' -BearerToken $SuperToken
  Assert-Exact 'replacement current criteria is readable' 200 $restored | Out-Null
  $final=$restored.Body | ConvertFrom-Json
  foreach($field in $fields){Assert-True 'current criteria thresholds restored through retained new version' ($final.$field -eq $current.$field)}
  $landAfter=Invoke-VerificationApi -Method GET -Path ("/api/lands/$($pinned.id)") -BearerToken $SuperToken
  Assert-Exact 'historical pinned land remains readable' 200 $landAfter | Out-Null
  Assert-True 'criteria replacement leaves historical land pin and measurements unchanged' (($landAfter.Body | ConvertFrom-Json | ConvertTo-Json -Depth 8 -Compress) -ceq ($pinned | ConvertTo-Json -Depth 8 -Compress))
  foreach($check in @(@{Path='assessments';Before=$assessmentBefore},@{Path='eligibility';Before=$eligibilityBefore})){
    $after=Invoke-VerificationApi -Method GET -Path ("/api/lands/$($pinned.id)/$($check.Path)") -BearerToken $SuperToken
    Assert-Exact 'historical evaluation remains readable after criteria changes' 200 $after | Out-Null
    Assert-True 'criteria replacement never rewrites previous evaluation evidence' ($after.Body -ceq $check.Before.Body)
  }

  $before=Invoke-VerificationApi -Method GET -Path ("/api/users/$InvestorId/approval") -BearerToken $SuperToken
  Assert-Exact 'investor decision baseline is authorized' 200 $before | Out-Null
  $beforeStatus=$before.Body | ConvertFrom-Json
  $reason="F1 governance $marker decision"
  $denied=Invoke-VerificationApi -Method POST -Path ("/api/users/$InvestorId/reject-approval") -BearerToken $InvestorToken -Json (@{reason=$reason}|ConvertTo-Json -Compress)
  Assert-Exact 'Investor cannot reject its own approval' 403 $denied | Out-Null
  $reject=Invoke-VerificationApi -Method POST -Path ("/api/users/$InvestorId/reject-approval") -BearerToken $adminToken -Json (@{reason=$reason}|ConvertTo-Json -Compress)
  Assert-Exact 'Admin records a reasoned investor rejection' 204 $reject | Out-Null
  $rejected=Invoke-VerificationApi -Method GET -Path ("/api/users/$InvestorId/approval") -BearerToken $SuperToken
  Assert-Exact 'rejected investor history is authorized' 200 $rejected | Out-Null
  $state=$rejected.Body | ConvertFrom-Json
  $decision=@($state.history | Where-Object {$_.eventType -eq 'AccountApprovalRejected' -and $_.details -eq $reason})
  Assert-True 'rejection records actual actor reason time and preserves prior decisions' (-not $state.isApproved -and @($state.history).Count -eq @($beforeStatus.history).Count+1 -and $decision.Count -eq 1 -and $decision[0].actorId -eq $adminId -and $decision[0].actorName -eq $adminName -and $decision[0].actorRole -eq 'Admin' -and $decision[0].actorSnapshotRecorded -and $decision[0].approvedAt)
  $approve=Invoke-VerificationApi -Method POST -Path ("/api/users/$InvestorId/approve") -BearerToken $adminToken
  Assert-Exact 'Admin reapproval restores investor access through legitimate workflow' 204 $approve | Out-Null
  $adminDetail=Invoke-VerificationApi -Method GET -Path ("/api/users/$adminId") -BearerToken $SuperToken
  Assert-Exact 'run-owned Admin profile is readable' 200 $adminDetail | Out-Null
  $profile=$adminDetail.Body | ConvertFrom-Json
  $rename=Invoke-VerificationApi -Method PUT -Path ("/api/users/$adminId") -BearerToken $SuperToken -Json (@{fullName=($profile.fullName+' renamed');email=$profile.email;phone=$profile.phone}|ConvertTo-Json -Compress)
  Assert-Exact 'authorized same-email run-owned Admin rename succeeds' 204 $rename | Out-Null
  $after=Invoke-VerificationApi -Method GET -Path ("/api/users/$InvestorId/approval") -BearerToken $SuperToken
  Assert-Exact 'approval history remains readable after actor rename' 200 $after | Out-Null
  $stable=@(($after.Body | ConvertFrom-Json).history | Where-Object {$_.eventId -eq $decision[0].eventId})
  Assert-True 'historical actor name remains the captured original after profile rename' ($stable.Count -eq 1 -and $stable[0].actorName -eq $adminName -and $stable[0].actorSnapshotRecorded)
}
