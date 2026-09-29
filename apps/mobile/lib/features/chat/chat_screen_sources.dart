part of 'chat_screen.dart';

extension _ChatScreenSources on _ChatScreenState {
  List<ConversationSourceChip> get _sourceChips => _attachedSources;

  Future<void> _loadConversationSources(String conversationId) async {
    try {
      final response = await _http.get<dynamic>(
        '/api/v1/conversations/$conversationId/sources',
      );
      final data = jsonObject(response.data);
      if (data == null || !mounted) return;
      final files = jsonMaps(data['files']);
      final collections = jsonMaps(data['collections']);
      setState(() {
        _attachedSources = [
          for (final file in files)
            ConversationSourceChip(
              id: '${file['fileId']}',
              label: file['fileName'] is String ? file['fileName'] as String : 'File',
              kind: 'file',
            ),
          for (final collection in collections)
            ConversationSourceChip(
              id: '${collection['collectionId']}',
              label: collection['name'] is String
                  ? collection['name'] as String
                  : 'Collection',
              kind: 'collection',
            ),
        ];
      });
    } on DioException {
      // Sources are optional; keep the composer usable without them.
    } catch (_) {}
  }

  Future<void> _detachSource(ConversationSourceChip source) async {
    final conversationId = _conversationId;
    if (conversationId == null) return;
    final path = source.kind == 'collection'
        ? '/api/v1/conversations/$conversationId/sources/collections/${source.id}'
        : '/api/v1/conversations/$conversationId/sources/files/${source.id}';
    try {
      await _http.delete<dynamic>(path);
      if (!mounted) return;
      setState(
        () => _attachedSources = _attachedSources
            .where((item) => item.id != source.id || item.kind != source.kind)
            .toList(),
      );
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() => _error = describeApiError(error));
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Could not update chat sources.');
    }
  }

  Future<void> _pickConversationSources() async {
    final conversationId = _conversationId;
    if (conversationId == null) return;
    try {
      final filesResponse = await _http.get<dynamic>('/api/v1/files');
      final collectionsResponse = await _http.get<dynamic>('/api/v1/collections');
      if (!mounted) return;
      final files = jsonMaps(filesResponse.data);
      final collections = jsonMaps(collectionsResponse.data);
      await showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        builder: (context) => SafeArea(
          child: DraggableScrollableSheet(
            expand: false,
            initialChildSize: 0.55,
            minChildSize: 0.35,
            maxChildSize: 0.9,
            builder: (context, scrollController) => ListView(
              controller: scrollController,
              padding: const EdgeInsets.fromLTRB(12, 12, 12, 24),
              children: [
                const Text(
                  'Attach sources',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 4),
                const Text(
                  'Jarvis searches only attached files and collections in this chat.',
                  style: TextStyle(color: JarvisColors.muted),
                ),
                const SizedBox(height: 12),
                if (files.isEmpty && collections.isEmpty)
                  const Text('Upload files from the Files screen first.'),
                for (final file in files)
                  ListTile(
                    leading: const Icon(PhosphorIconsRegular.fileText),
                    title: Text('${file['fileName']}'),
                    subtitle: Text('${file['processingStatus']}'),
                    onTap: () async {
                      Navigator.pop(context);
                      await _http.post<dynamic>(
                        '/api/v1/conversations/$conversationId/sources/files/${file['id']}',
                      );
                      await _loadConversationSources(conversationId);
                    },
                  ),
                for (final collection in collections)
                  ListTile(
                    leading: const Icon(PhosphorIconsRegular.folders),
                    title: Text('${collection['name']}'),
                    subtitle: Text('${(collection['fileIds'] is List ? (collection['fileIds'] as List).length : 0)} files'),
                    onTap: () async {
                      Navigator.pop(context);
                      await _http.post<dynamic>(
                        '/api/v1/conversations/$conversationId/sources/collections/${collection['id']}',
                      );
                      await _loadConversationSources(conversationId);
                    },
                  ),
              ],
            ),
          ),
        ),
      );
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() => _error = describeApiError(error));
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Could not load attachable sources.');
    }
  }

  Future<void> _openCitation(MessageCitation citation) async {
    if (citation.sourceStatus == 'deleted') {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('This source file was deleted.')),
      );
      return;
    }
    try {
      final response = await _http.get<dynamic>(
        '/api/v1/files/citations/${citation.chunkId}',
      );
      final data = jsonObject(response.data);
      if (data == null || !mounted) return;
      final status = data['sourceStatus'] ?? data['SourceStatus'];
      if (status is String && status != 'available') {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Source is $status.')),
        );
        return;
      }
      await file_download.downloadAndOpen(_http, citation.fileId, citation.displayName);
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(describeApiError(error))),
      );
    } catch (_) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Could not open this citation.')),
      );
    }
  }
}
