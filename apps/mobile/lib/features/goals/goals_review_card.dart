import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'goal_screen.dart';

/// Goals that have something to look at in the weekly review: a check-in that
/// is due, or a keep / change / stop suggestion from the owner's own
/// check-ins. Hidden when there is nothing, and never more insistent for being
/// ignored (the suggestion has its own "Not now").
class GoalsReviewCard extends StatefulWidget {
  const GoalsReviewCard({required this.http, super.key});

  final Dio http;

  @override
  State<GoalsReviewCard> createState() => _GoalsReviewCardState();
}

class _GoalsReviewCardState extends State<GoalsReviewCard> {
  List<Map<String, dynamic>> _goals = const [];

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/goals');
      if (!mounted) return;
      setState(() {
        _goals = [
          for (final goal in jsonMaps(response.data))
            if (jsonString(goal, 'id') != null &&
                goal['status'] == 'active' &&
                (goal['checkInDue'] == true || goal['review'] != null))
              goal,
        ];
      });
    } on DioException {
      // A bonus on the review: without goals there is simply no card.
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_goals.isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: ContentWidth(
        child: Column(
          key: const Key('goals-review'),
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const GroupLabel('Goals'),
            for (final (i, goal) in _goals.indexed)
              FeatureRow(
                key: Key('goals-review-${goal['id']}'),
                index: i,
                icon: PhosphorIconsRegular.target,
                title: asJsonString(goal['title']) ?? 'Goal',
                subtitle:
                    asJsonString(jsonObject(goal['review'])?['reason']) ??
                    'A check-in is due, whenever you like.',
                trailing: const Icon(Icons.chevron_right, size: 20),
                onTap: () async {
                  await Navigator.of(context).push<void>(
                    MaterialPageRoute(
                      builder: (_) => GoalScreen(
                        http: widget.http,
                        goalId: goal['id'] as String,
                      ),
                    ),
                  );
                  if (mounted) unawaited(_load());
                },
              ),
          ],
        ),
      ),
    );
  }
}
