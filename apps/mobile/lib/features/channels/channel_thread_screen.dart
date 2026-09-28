part of 'channels_screen.dart';

class ChannelThreadScreen extends StatefulWidget {
  const ChannelThreadScreen({
    required this.http,
    required this.channelId,
    required this.peer,
    super.key,
  });

  final Dio http;
  final String channelId;
  final String peer;

  @override
  State<ChannelThreadScreen> createState() => _ChannelThreadScreenState();
}

class _ChannelThreadScreenState extends State<ChannelThreadScreen> {
  List<Map<String, dynamic>> _messages = const [];
  bool _loading = true;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/channels/${widget.channelId}/threads/${Uri.encodeComponent(widget.peer)}/messages',
      );
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _messages = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this thread.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load this thread.';
      });
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(widget.peer)),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _messages.isEmpty,
      onRetry: () => unawaited(_load()),
      empty: const EmptyState(
        icon: PhosphorIconsRegular.chatCircle,
        title: 'No messages yet',
        message: 'Messages with this number will appear here.',
      ),
      child: RefreshIndicator(
        onRefresh: _load,
        child: ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          itemCount: _messages.length,
          itemBuilder: (context, index) {
            final message = _messages[index];
            return Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: SurfaceCard(
                padding: const EdgeInsets.all(14),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '${asJsonString(message['direction']) == 'out' ? 'Jarvis' : asJsonString(message['peer']) ?? widget.peer} · ${asJsonString(message['status']) ?? ''}',
                      style: Theme.of(context).textTheme.labelSmall?.copyWith(
                        color: JarvisColors.muted,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(asJsonString(message['text']) ?? ''),
                  ],
                ),
              ),
            );
          },
        ),
      ),
    ),
  );
}
