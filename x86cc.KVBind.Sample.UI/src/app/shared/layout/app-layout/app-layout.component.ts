import { Component } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { UserService } from '../../../core/services/user.service';
import { ThemeService } from '../../../core/services/theme.service';
import { PerfPanelComponent } from '../../components/perf-panel.component';

@Component({
  selector: 'app-layout',
  imports: [RouterModule, PerfPanelComponent],
  templateUrl: './app-layout.component.html',
})
export class AppLayoutComponent {
  constructor(
    readonly userService: UserService,
    readonly theme: ThemeService,
    private readonly router: Router,
  ) {}

  logout(): void {
    this.userService.clearUser();
    this.router.navigate(['/login']);
  }
}
