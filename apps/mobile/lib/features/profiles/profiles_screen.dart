import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

part 'profile_editor_sheet.dart';

class ProfilesScreen extends StatefulWidget {
  const ProfilesScreen({required this.http, super.key});

  final Dio http;

  @override
  State<ProfilesScreen> createState() => _ProfilesScreenState();
}

class _ProfilesScreenState extends State<ProfilesScreen> {
  List<Map<String, dynamic>> _profiles = const [];
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
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/profiles');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _profiles = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = firstProblemMessage(error.response?.data) ?? 'Could not load profiles.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load profiles.';
      });
    }
  }

  Future<void> _edit([Map<String, dynamic>? profile]) async {
    final saved = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => ProfileEditorSheet(http: widget.http, profile: profile),
      ),
    );
    if (saved == true && mounted) unawaited(_load());
  }

  Future<void> _delete(Map<String, dynamic> profile) async {
    final id = jsonString(profile, 'id');
    if (id == null) return;
    if (asJsonBool(profile['isDefault'])) {
      setState(() => _error = 'The default profile cannot be deleted.');
      return;
    }
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete profile?',
      message:
          'Conversations already using “${asJsonString(profile['name']) ?? 'this profile'}” keep the snapshot they started with.',
      cancelLabel: 'Keep profile',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    try {
      await widget.http.delete('/api/v1/profiles/$id');
      await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error = firstProblemMessage(error.response?.data) ??
            'Jarvis could not delete this profile.',
      );
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Profiles'),
      actions: [
        HeaderAction(
          label: 'New',
          icon: PhosphorIconsRegular.plus,
          onPressed: () => unawaited(_edit()),
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _profiles.isEmpty,
      onRetry: _load,
      onRefresh: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.userCircle,
        title: 'No profiles yet.',
        message: 'Jarvis will create a default profile on first use.',
      ),
      child: ListView.builder(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        itemCount: _profiles.length,
        itemBuilder: (context, index) {
          final profile = _profiles[index];
          final id = jsonString(profile, 'id');
          if (id == null) return const SizedBox.shrink();
          final name = asJsonString(profile['name']) ?? 'Untitled profile';
          final description = asJsonString(profile['description']);
          final isDefault = asJsonBool(profile['isDefault']);
          return ContentWidth(
            child: SurfaceCard(
              margin: const EdgeInsets.only(bottom: 8),
              onTap: () => unawaited(_edit(profile)),
              child: Row(
                children: [
                  IconBadge(
                    icon: isDefault
                        ? PhosphorIconsFill.userCircle
                        : PhosphorIconsRegular.userCircle,
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.titleSmall
                              ?.copyWith(fontSize: 15),
                        ),
                        const SizedBox(height: 3),
                        Text(
                          [
                            if (isDefault) 'Default',
                            if (description != null && description.isNotEmpty)
                              description,
                            if (asJsonBool(profile['restrictSkills']))
                              'Limited skills',
                            if (asJsonBool(profile['restrictFiles']))
                              'Limited files',
                            'Memory: ${asJsonString(profile['memoryScope']) ?? 'all'}',
                          ].join(' · '),
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                  if (!isDefault)
                    IconButton(
                      tooltip: 'Delete profile',
                      onPressed: () => unawaited(_delete(profile)),
                      icon: const Icon(PhosphorIconsRegular.trash, size: 20),
                    ),
                ],
              ),
            ),
          );
        },
      ),
    ),
  );
}
