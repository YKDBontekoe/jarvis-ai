import 'package:dio/dio.dart';

import '../api/api_errors.dart';
import '../json_maps.dart';

final emailPattern = RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$');

bool passwordMeetsPolicy(String password) =>
    password.length >= 8 &&
    password.length <= 128 &&
    password.contains(RegExp(r'[A-Z]')) &&
    password.contains(RegExp(r'[a-z]')) &&
    password.contains(RegExp(r'[0-9]'));

String? signInFormError({
  required String email,
  required String password,
  required bool creatingAccount,
}) {
  if (!emailPattern.hasMatch(email)) return 'Enter a valid email address.';
  if (password.isEmpty || password.length > 128) return 'Enter your password.';
  if (creatingAccount && !passwordMeetsPolicy(password)) {
    return 'Use 8 to 128 characters with an uppercase letter, a lowercase letter, and a number.';
  }
  return null;
}

String accountErrorMessage(DioException error) {
  final data = error.response?.data;
  final message = asJsonString(jsonObject(data)?['message']);
  if (message != null && message.isNotEmpty) return message;
  final problem = firstProblemMessage(data);
  if (problem != null) return problem;
  if (error.response?.statusCode == 401) {
    return 'Invalid email or password.';
  }
  return describeApiError(error);
}
