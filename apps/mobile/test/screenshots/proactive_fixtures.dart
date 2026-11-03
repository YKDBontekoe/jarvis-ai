const proactiveCaseId = '019a4320-0000-7000-8000-000000000001';
const proactiveTripCaseId = '019a4320-0000-7000-8000-000000000002';
const proactiveCapacityCaseId = '019a4320-0000-7000-8000-000000000003';
const proactiveTripRef = 'trip:019a4320-0000-7000-8000-000000000004';
const proactiveWatchRef = 'watch:019a4320-0000-7000-8000-000000000005';

List<Map<String, Object?>> proactiveSources() => [
  {
    'ref': proactiveTripRef,
    'title': 'Lisbon weekend',
    'version': 'trip-v1',
    'state': 'planned',
    'detail': 'A confirmed flight and a museum visit. Review the current transport source before changing a booking.',
    'tripItems': [
      {
        'id': 'flight',
        'title': 'Flight to Lisbon',
        'date': '2026-11-05',
        'start': '09:00:00',
        'end': '11:00:00',
        'kind': 'travel',
        'verified': true,
      },
      {
        'id': 'museum',
        'title': 'Museum visit',
        'date': '2026-11-05',
        'start': '11:15:00',
        'end': '12:15:00',
        'kind': 'activity',
        'verified': true,
      },
    ],
  },
  {
    'ref': proactiveWatchRef,
    'title': 'Published flight delay',
    'version': 'watch-v2',
    'state': 'active',
    'numericValue': 60,
    'detail': 'The selected source reports a 60-minute delay.',
    'at': DateTime.now().toUtc().toIso8601String(),
  },
];

Map<String, Object?> proactiveTripWork() => {
  ...proactiveWork(),
  'id': proactiveTripCaseId,
  'kind': 'trip',
  'title': 'Keep the trip useful',
  'objective': 'Watch the chosen transport and prepare the affected itinerary.',
  'nextAction': 'Review the flight shift and the proposed museum time.',
  'stopCondition': 'The trip is finished and I confirm the outcome.',
  'rootRef': proactiveTripRef,
  'sourceRefs': [proactiveTripRef, proactiveWatchRef],
  'context': proactiveSources(),
  'steps': <Object>[],
  'assumptions': <Object>[],
  'findings': [
    {
      'key': 'flight',
      'title': 'The flight schedule changed',
      'reason': 'A 60-minute delay affects the museum visit. Review the revised preparation.',
    },
  ],
  'preparedReport': 'The selected source reports a 60-minute shift: flight 10:00–12:00. Propose the museum visit at 12:15–13:15 after the transfer. Verify the venue and reservations before adopting this plan. No booking has changed.',
};

Map<String, Object?> proactiveCapacityWork() => {
  ...proactiveWork(),
  'id': proactiveCapacityCaseId,
  'kind': 'goal_capacity',
  'title': 'Keep the week realistic',
  'objective': 'Allocate one shared week across health and learning.',
  'steps': <Object>[],
  'assumptions': <Object>[],
  'nextAction': 'Health has priority. Reduce the learning step to fit the remaining week.',
  'stopCondition': 'I stop this weekly responsibility.',
  'availableMinutes': 120,
  'policy': {
    'timeZoneId': 'Europe/Amsterdam',
    'cadenceMinutes': 60,
    'maxChecksPerDay': 12,
    'quietStart': 22,
    'quietEnd': 8,
    'optionalItemsPerDay': 3,
    'useCalendarCapacity': true,
  },
  'findings': <Object>[],
  'preparedReport': 'Shared weekly budget: 120 minutes. Calendar availability leaves 90 minutes. Health: 60 of 60 minutes fit. Learning: 30 of 60 minutes fit. Reduce or defer the remaining 30 minutes; no goal has been changed.',
};

Map<String, Object?> proactiveWork() => {
  'id': proactiveCaseId,
  'kind': 'project',
  'title': 'Move the renovation forward',
  'objective': 'Keep the quotes, measurements and decisions moving together.',
  'status': 'open',
  'enabled': true,
  'revision': 3,
  'nextAction': 'Choose a quote. The measurements are ready.',
  'stopCondition': 'The renovation is finished and I confirm the result.',
  'lastCheckedAt': DateTime.now()
      .subtract(const Duration(minutes: 20))
      .toUtc()
      .toIso8601String(),
  'nextCheckAt': DateTime.now()
      .add(const Duration(minutes: 40))
      .toUtc()
      .toIso8601String(),
  'sourceRefs': <Object>[],
  'context': <Object>[],
  'steps': [
    {
      'id': 'measure',
      'title': 'Confirm the measurements',
      'done': true,
      'dependsOn': <Object>[],
    },
    {
      'id': 'choose',
      'title': 'Choose a quote',
      'done': false,
      'dependsOn': ['measure'],
      'minutes': 30,
    },
  ],
  'findings': [
    {
      'key': 'next',
      'title': 'Ready for your decision',
      'reason': 'The measurements are complete. The quote decision is the next eligible step.',
    },
  ],
  'preparedReport': 'Measurements are complete. No messages have been sent and no supplier has been chosen.',
  'receipts': [
    {
      'id': 'receipt-1',
      'kind': 'prepared',
      'reason': 'Checked permitted sources and refreshed the next step.',
      'at': 'Today, 12:00',
      'undone': false,
    },
  ],
};
