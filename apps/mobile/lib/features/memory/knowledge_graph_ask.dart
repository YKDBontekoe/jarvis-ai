part of 'knowledge_graph_screen.dart';

/// Questions about the graph: asks the server, keeps the conversation for follow-ups, and lights up the answer.
mixin _KnowledgeGraphAsk on _KnowledgeGraphController {
  Future<void> _ask(String raw) async {
    final question = raw.trim();
    if (question.isEmpty || _asking) return;
    final revision = ++_askRevision;
    setState(() {
      _asking = true;
      _askError = null;
      _pendingQuestion = question;
    });
    _askField.clear();
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/graph/ask',
        data: {
          'question': question,
          'focusIds': [?_selectedId],
          'history': [
            for (final turn in _turns.reversed.take(4).toList().reversed)
              {'question': turn.question, 'answer': turn.answer},
          ],
        },
      );
      if (!mounted || revision != _askRevision) return;
      final answer = parseGraphAnswer(
        question,
        jsonObject(response.data) ?? const {},
      );
      setState(() {
        _asking = false;
        _pendingQuestion = null;
        _turns.add(answer);
        _answer = answer;
        _selectedId = null;
        _reveal++;
      });
      _focusAnswer(answer);
      if (!MediaQuery.disableAnimationsOf(context)) {
        unawaited(_pulse.forward(from: 0));
      }
    } on DioException catch (error) {
      if (!mounted || revision != _askRevision) return;
      setState(() {
        _asking = false;
        _pendingQuestion = null;
        _askError =
            firstProblemMessage(error.response?.data) ??
            'Could not answer that right now.';
      });
    }
  }

  void _clearAnswer() {
    _askRevision++;
    setState(() {
      _answer = null;
      _turns.clear();
      _asking = false;
      _askError = null;
      _pendingQuestion = null;
    });
  }

  /// Moves the 2D camera to frame the answer's entities.
  void _focusAnswer(GraphAnswerView answer) {
    if (_viewport.isEmpty) return;
    final points = [
      for (final id in answer.entityIds)
        if (_snapshot.node(id) != null) _worldPoint(id),
    ];
    if (points.isEmpty) return;
    var bounds = Rect.fromPoints(points.first, points.first);
    for (final point in points) {
      bounds = bounds.expandToInclude(Rect.fromPoints(point, point));
    }
    // Screen margins rather than world ones, so labels and the zoom buttons on the right never cover the answer.
    final visible = Rect.fromLTRB(
      _visibleRect().left + 48,
      _visibleRect().top + 24,
      _visibleRect().right - 72,
      _visibleRect().bottom - 36,
    );
    final scale = math
        .min(
          visible.width / math.max(bounds.width, 1),
          visible.height / math.max(bounds.height, 1),
        )
        .clamp(_minScale, 1.4);
    _animateTo(
      _placed(
        scale,
        visible.center.dx - bounds.center.dx * scale,
        visible.center.dy - bounds.center.dy * scale,
      ),
    );
  }

  @override
  Widget _askPanel() {
    final colors = JarvisColors.of(context);
    final answer = _answer;
    final suggestions = answer != null && answer.followUps.isNotEmpty
        ? answer.followUps
        : graphQuestionSuggestions(
            _snapshot,
            _degrees,
            selected: _snapshot.node(_selectedId),
          );
    return Column(
      key: const Key('graph-ask'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          key: const Key('graph-ask-field'),
          controller: _askField,
          textInputAction: TextInputAction.send,
          minLines: 1,
          maxLines: 3,
          maxLength: 500,
          onSubmitted: (value) => unawaited(_ask(value)),
          decoration: InputDecoration(
            hintText: answer == null
                ? 'Ask about your graph…'
                : 'Ask a follow-up…',
            isDense: true,
            counterText: '',
            prefixIcon: Icon(
              PhosphorIconsRegular.sparkle,
              color: colors.accent,
            ),
            suffixIcon: _asking
                ? const Padding(
                    padding: EdgeInsets.all(12),
                    child: SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  )
                : IconButton(
                    key: const Key('graph-ask-send'),
                    tooltip: 'Ask',
                    onPressed: () => unawaited(_ask(_askField.text)),
                    icon: const Icon(PhosphorIconsRegular.paperPlaneTilt),
                  ),
          ),
        ),
        if (_pendingQuestion case final pending?) ...[
          const SizedBox(height: 10),
          Text(
            'Thinking about “$pending”…',
            style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
          ),
        ],
        if (_askError case final error?) ...[
          const SizedBox(height: 8),
          Text(error, style: TextStyle(fontSize: 12.5, color: colors.warning)),
        ],
        if (answer != null) ...[
          const SizedBox(height: 12),
          _answerCard(answer),
        ],
        if (!_asking && suggestions.isNotEmpty) ...[
          const SizedBox(height: 10),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final suggestion in suggestions)
                ActionChip(
                  key: Key('graph-suggestion-$suggestion'),
                  label: Text(
                    suggestion,
                    style: const TextStyle(fontSize: 12.5),
                  ),
                  avatar: Icon(
                    PhosphorIconsRegular.sparkle,
                    size: 14,
                    color: colors.accent,
                  ),
                  visualDensity: VisualDensity.compact,
                  onPressed: () => unawaited(_ask(suggestion)),
                ),
            ],
          ),
        ],
      ],
    );
  }

  Widget _answerCard(GraphAnswerView answer) {
    final colors = JarvisColors.of(context);
    final entities = [for (final id in answer.entityIds) ?_snapshot.node(id)];
    // A Material, not a decorated box, so the facts tile can draw its ink on it.
    return Material(
      key: const Key('graph-answer'),
      color: colors.accentSoft.withValues(alpha: colors.isDark ? .6 : 1),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: BorderSide(color: colors.accent.withValues(alpha: .25)),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(14, 12, 8, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Text(
                    answer.question,
                    style: TextStyle(
                      fontSize: 12.5,
                      fontWeight: FontWeight.w600,
                      color: colors.inkSoft,
                    ),
                  ),
                ),
                IconButton(
                  key: const Key('graph-answer-clear'),
                  tooltip: 'Clear answer',
                  visualDensity: VisualDensity.compact,
                  onPressed: _clearAnswer,
                  icon: const Icon(PhosphorIconsRegular.x, size: 16),
                ),
              ],
            ),
            Padding(
              padding: const EdgeInsets.only(right: 8),
              child: SelectableText(answer.answer),
            ),
            if (!answer.grounded)
              Padding(
                padding: const EdgeInsets.only(top: 6, right: 8),
                child: Text(
                  'No model was available, so these are the facts found.',
                  style: TextStyle(fontSize: 12, color: colors.muted),
                ),
              ),
            if (entities.isNotEmpty) ...[
              const SizedBox(height: 10),
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  for (final node in entities.take(12))
                    InputChip(
                      key: Key('answer-entity-${node.name}'),
                      avatar: Icon(
                        graphTypeIcon(node.type),
                        size: 14,
                        color: graphTypeColor(node.type, context),
                      ),
                      label: Text(
                        node.name,
                        style: const TextStyle(fontSize: 12),
                      ),
                      visualDensity: VisualDensity.compact,
                      onPressed: () => _select(node.id, toggle: false),
                    ),
                ],
              ),
            ],
            if (answer.facts.isNotEmpty)
              Theme(
                data: Theme.of(
                  context,
                ).copyWith(dividerColor: Colors.transparent),
                child: ExpansionTile(
                  key: const Key('graph-answer-facts'),
                  tilePadding: EdgeInsets.zero,
                  childrenPadding: const EdgeInsets.only(bottom: 4),
                  dense: true,
                  title: Text(
                    'Based on ${answer.facts.length} ${answer.facts.length == 1 ? 'fact' : 'facts'}',
                    style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
                  ),
                  children: [
                    for (final fact in answer.facts)
                      Align(
                        alignment: Alignment.centerLeft,
                        child: Padding(
                          padding: const EdgeInsets.symmetric(vertical: 2),
                          child: Text(
                            '• $fact',
                            style: TextStyle(
                              fontSize: 12.5,
                              color: colors.inkSoft,
                            ),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }
}
