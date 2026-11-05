1. Linux clean-HEAD unit trx and the Windows (machine culture ar-SA) unit trx of the same HEAD: 0 failures each,
   same test count.
2. TestCultureGuardTests (thread and thread-pool culture = ar-SY, Gregorian) pass on both; removing TestCulture
   turns them red, and TestCultureInitializerPresenceTests checks the initializer in all three test projects.