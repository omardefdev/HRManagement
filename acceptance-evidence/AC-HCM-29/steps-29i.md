1. HR.Core no longer publishes ICandidateAccepted for internal mobility (IInternalPlacementCompleted only — unit and
   integration tests). Defence in depth in HR.Contracts:
2. An ICandidateAccepted v4 with IsInternalMobility=true is published on the acceptance broker for agency A.
3. A control message (same shape, IsInternalMobility=false) for another employee opens an Appointment.
4. AC-29i: no ServiceTransaction for the mobility employee; the control employee has one.