import { Component, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatTableModule, MatTableDataSource } from '@angular/material/table';
import { MatPaginatorModule, MatPaginator } from '@angular/material/paginator';
import { MatSortModule, MatSort } from '@angular/material/sort';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { JobDescriptionService } from '../../services/job-description';
import { JobDescription, JobDescriptionSubmission } from '../../models/job-description';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule
  ],
  templateUrl: './admin.html',
  styleUrl: './admin.scss'
})
export class AdminComponent implements OnInit {
  displayedColumns: string[] = ['title', 'company', 'location', 'timestamp', 'actions'];
  dataSource = new MatTableDataSource<JobDescription>([]);
  jobForm: FormGroup;
  isLoading = false;
  isSubmitting = false;
  totalCount = 0;
  currentPage = 1;
  pageSize = 10;

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(
    private fb: FormBuilder,
    private jobDescriptionService: JobDescriptionService,
    private snackBar: MatSnackBar
  ) {
    this.jobForm = this.fb.group({
      Title: ['', Validators.required],
      Company: ['', Validators.required],
      Description: ['', Validators.required],
      Location: [''],
      Salary: [''],
      WorkdayId: ['']
    });
  }

  ngOnInit(): void {
    this.loadJobDescriptions();
  }

  ngAfterViewInit() {
    this.dataSource.paginator = this.paginator;
    this.dataSource.sort = this.sort;
    
    // Handle pagination events
    if (this.paginator) {
      this.paginator.page.subscribe(event => {
        this.currentPage = event.pageIndex + 1;
        this.pageSize = event.pageSize;
        this.loadJobDescriptions();
      });
    }
  }

  loadJobDescriptions() {
    this.isLoading = true;
    this.jobDescriptionService.getJobDescriptions(this.currentPage, this.pageSize)
      .subscribe({
        next: (response) => {
          this.dataSource.data = response.items;
          this.totalCount = response.totalCount;
          if (this.paginator) {
            this.paginator.length = response.totalCount;
          }
          this.isLoading = false;
        },
        error: (error) => {
          console.error('Error loading job descriptions', error);
          this.snackBar.open('Error loading job descriptions', 'Close', { duration: 3000 });
          this.isLoading = false;
        }
      });
  }

  applyFilter(event: Event) {
    const filterValue = (event.target as HTMLInputElement).value;
    this.dataSource.filter = filterValue.trim().toLowerCase();

    if (this.dataSource.paginator) {
      this.dataSource.paginator.firstPage();
    }
  }

  onSubmit() {
    if (this.jobForm.valid) {
      this.isSubmitting = true;
      const submission: JobDescriptionSubmission = this.jobForm.value;
      
      this.jobDescriptionService.submitJobDescription(submission)
        .subscribe({
          next: (response) => {
            this.snackBar.open('Job description submitted successfully', 'Close', { duration: 3000 });
            this.resetForm();
            this.loadJobDescriptions();
            this.isSubmitting = false;
          },
          error: (error) => {
            console.error('Error submitting job description', error);
            this.snackBar.open('Error submitting job description', 'Close', { duration: 3000 });
            this.isSubmitting = false;
          }
        });
    }
  }

  resetForm() {
    this.jobForm.reset();
  }

  deleteJobDescription(id: string) {
    if (confirm('Are you sure you want to delete this job description?')) {
      this.isLoading = true;
      this.jobDescriptionService.deleteJobDescription(id)
        .subscribe({
          next: (response) => {
            this.snackBar.open('Job description deleted successfully', 'Close', { duration: 3000 });
            this.loadJobDescriptions();
          },
          error: (error) => {
            console.error('Error deleting job description', error);
            this.snackBar.open('Error deleting job description', 'Close', { duration: 3000 });
            this.isLoading = false;
          }
        });
    }
  }
}