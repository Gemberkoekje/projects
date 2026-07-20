---
source_file: "tests/SpaceTraders.Domain.Tests/Aggregates/ContractTests.cs"
type: "code"
community: "Community 203"
location: "L9"
tags:
  - graphify/code
  - graphify/EXTRACTED
  - community/Community_203
---

# ContractTests

## Connections
- [[.Accept_AlreadyAccepted_DoesNotRaiseEventAgain()]] - `method` [EXTRACTED]
- [[.Accept_NotYetAccepted_RaisesContractAcceptedEvent()]] - `method` [EXTRACTED]
- [[.CheckDeadline_AlreadyPassed_DoesNotRaiseEvent()]] - `method` [EXTRACTED]
- [[.CheckDeadline_MoreThan24HoursAway_DoesNotRaiseEvent()]] - `method` [EXTRACTED]
- [[.CheckDeadline_Within24Hours_RaisesDeadlineApproachingEvent()]] - `method` [EXTRACTED]
- [[.CreateContract()]] - `method` [EXTRACTED]
- [[.Fulfill_AlreadyFulfilled_DoesNotRaiseEventAgain()]] - `method` [EXTRACTED]
- [[.Fulfill_NotYetFulfilled_RaisesContractFulfilledEvent()]] - `method` [EXTRACTED]
- [[ContractTests.cs]] - `contains` [EXTRACTED]

#graphify/code #graphify/EXTRACTED #community/Community_203