import { Redirect } from 'expo-router';

// Unknown paths (including OAuth callbacks and a web build opened from a sub-path)
// go back to the start; the root layout's guards pick the right screen from there.
export default function NotFound() {
  return <Redirect href="/" />;
}
