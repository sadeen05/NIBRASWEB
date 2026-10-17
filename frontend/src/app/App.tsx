import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App as AntApp } from 'antd';
import { RouterProvider } from 'react-router-dom';

import { AuthProvider } from '@/auth/AuthProvider';
import { AppTheme } from '@/theme/AppTheme';
import { router } from '@/app/router';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 30_000,
    },
  },
});

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AppTheme>
        <AntApp>
          <AuthProvider>
            <RouterProvider router={router} />
          </AuthProvider>
        </AntApp>
      </AppTheme>
    </QueryClientProvider>
  );
}
