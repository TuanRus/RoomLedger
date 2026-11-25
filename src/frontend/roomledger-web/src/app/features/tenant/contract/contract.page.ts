import { NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, OnInit, inject, input, signal, viewChild } from '@angular/core';
import { FormField, form, validate } from '@angular/forms/signals';

import { Viewport } from '../../../core/layout/viewport';
import { VndPipe } from '../../../shared/pipes/format.pipes';
import { Icon } from '../../../shared/ui/icon';
import { REASON_MIN, TenantContractStore } from './tenant-contract.store';

/**
 * TN-10: the tenant reads the scanned contract and the terms the landlord typed in, then confirms them
 * or says what is wrong (at least 10 characters, sent to the landlord). Computer: scan left, terms right.
 * ?sample=confirmed shows the contract as already confirmed.
 */
@Component({
  selector: 'app-tenant-contract-page',
  imports: [NgOptimizedImage, NgTemplateOutlet, FormField, Icon, VndPipe],
  templateUrl: './contract.page.html',
  styleUrl: './contract.page.scss',
})
export class TenantContractPage implements OnInit {
  readonly sample = input<string>(); // ?sample= (not ?state=, which the OIDC client reads as a sign-in callback)

  private readonly store = inject(TenantContractStore);
  protected readonly viewport = inject(Viewport);
  protected readonly contract = this.store.contract;
  protected readonly decision = this.store.decision;
  protected readonly min = REASON_MIN;

  /** "Không đúng" was pressed: show the reason field. */
  protected readonly rejecting = signal(false);
  protected readonly model = signal({ reason: '' });
  protected readonly f = form(this.model, (p) => {
    validate(p.reason, ({ value }) =>
      value().trim().length < REASON_MIN ? { kind: 'minLength', message: `Ghi ít nhất ${REASON_MIN} ký tự để chủ nhà biết chỗ nào chưa đúng.` } : undefined,
    );
  });

  private readonly scanDialog = viewChild<ElementRef<HTMLDialogElement>>('scanDialog');
  private readonly reasonBox = viewChild<ElementRef<HTMLTextAreaElement>>('reasonBox');

  ngOnInit(): void {
    if (this.sample() === 'confirmed') this.store.showConfirmed();
  }

  protected startReject(): void {
    this.rejecting.set(true);
    setTimeout(() => this.reasonBox()?.nativeElement.focus());
  }

  protected cancelReject(): void {
    this.rejecting.set(false);
    this.model.set({ reason: '' });
    this.f().reset();
  }

  protected sendReject(event: Event): void {
    event.preventDefault();
    this.f().markAsTouched();
    if (this.f().invalid()) return;
    this.store.reject(this.model().reason);
    this.rejecting.set(false);
  }

  protected confirm(): void {
    this.store.confirm();
  }

  protected review(): void {
    this.cancelReject();
    this.store.reset();
  }

  protected openScan(): void {
    this.scanDialog()?.nativeElement.showModal();
  }

  protected closeScan(): void {
    this.scanDialog()?.nativeElement.close();
  }
}
