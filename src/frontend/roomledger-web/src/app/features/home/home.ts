import { Component, inject, OnInit } from '@angular/core';
import { JsonPipe } from '@angular/common';

import { SessionService } from '../../core/session/session.service';

@Component({
  selector: 'app-home',
  imports: [JsonPipe],
  templateUrl: './home.html',
})
export class HomeComponent implements OnInit {
  readonly session = inject(SessionService);

  ngOnInit(): void {
    this.session.loadMe();
  }
}
