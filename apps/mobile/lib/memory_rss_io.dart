import 'dart:io' show ProcessInfo;

/// Resident and peak memory of this process, in bytes.
Map<String, Object> processMemory() => {
  'rssBytes': ProcessInfo.currentRss,
  'maxRssBytes': ProcessInfo.maxRss,
};
