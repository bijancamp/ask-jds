import { Routes } from '@angular/router';
import { HomeComponent } from './pages/home/home';
import { AdminComponent } from './pages/admin/admin';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'admin', component: AdminComponent },
  { path: '**', redirectTo: '' }
];
