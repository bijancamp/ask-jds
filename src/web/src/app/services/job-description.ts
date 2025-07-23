import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { JobDescription, JobDescriptionSubmission } from '../models/job-description';

@Injectable({
  providedIn: 'root'
})
export class JobDescriptionService {
  private apiUrl = 'http://localhost:3100';

  constructor(private http: HttpClient) { }

  getJobDescriptions(pageNumber: number = 1, pageSize: number = 10, search?: string): Observable<{
    totalCount: number;
    pageSize: number;
    pageNumber: number;
    items: JobDescription[];
  }> {
    let url = `${this.apiUrl}/jobdescriptions?pageNumber=${pageNumber}&pageSize=${pageSize}`;
    
    if (search) {
      url += `&search=${encodeURIComponent(search)}`;
    }
    
    return this.http.get<{
      totalCount: number;
      pageSize: number;
      pageNumber: number;
      items: JobDescription[];
    }>(url);
  }

  submitJobDescription(jobDescription: JobDescriptionSubmission): Observable<{ id: string; message: string }> {
    return this.http.post<{ id: string; message: string }>(`${this.apiUrl}/jobdescription`, jobDescription);
  }

  deleteJobDescription(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/jobdescription/${id}`);
  }
}